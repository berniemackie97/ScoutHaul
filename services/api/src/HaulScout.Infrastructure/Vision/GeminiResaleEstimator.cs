using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Vision;

/// <summary>
/// Gemini with Google Search grounding: finds recent sold and active listings for a product and reads
/// a used price range from them. Answers are cached by description so a repeat scan costs no quota.
/// </summary>
internal sealed class GeminiResaleEstimator(
    HttpClient httpClient,
    IConfiguration configuration,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<GeminiResaleEstimator> logger) : IResaleEstimator
{
    private static readonly TimeSpan FoundTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NothingFoundTtl = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 3;

    private string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]);

    private const string Prompt =
        "You are a reseller pricing a secondhand item. Use Google Search to find REAL, RECENT listings for the " +
        "product below, then give its USED resale value in USD. Never use retail, MSRP or optimistic asking prices.\n" +
        "Look for eBay sold/completed listings first. Mark a listing \"sold\" only when the page clearly shows it " +
        "sold; otherwise mark it \"active\". Active listings on eBay, Poshmark, Mercari, Depop, Facebook " +
        "Marketplace or StockX are weaker support.\n" +
        "Different products are not comps: raw vs graded, other languages, proxies/reprints/customs, lots and " +
        "bundles, sealed vs opened, other card numbers, sizes, colorways, model numbers or editions. Leave them out " +
        "unless the item facts say that exact variant.\n" +
        "low = quick-sale or lower-condition value; high = realistic value in matching condition, not a rare outlier.\n" +
        "comps: the 3-8 listings you actually found. For each give the price, status, condition, the listing title " +
        "as shown, the sold or listed date as YYYY-MM-DD if the page shows one, and its URL. Report only what the " +
        "search returned; never invent a listing, price, date or URL.\n" +
        "Respond with ONLY a JSON object (no prose, no markdown fences) of shape: " +
        "{\"low\":number,\"high\":number,\"priceRangeType\":\"tight|moderate|wide\"," +
        "\"valuationConfidence\":\"high|medium|low\",\"keyValueFactors\":[string]," +
        "\"missingInformation\":[string],\"recommendedNextSteps\":[string],\"rationale\":string," +
        "\"comps\":[{\"price\":number,\"status\":\"sold|active\",\"condition\":string,\"title\":string," +
        "\"date\":string,\"note\":string,\"sourceUrl\":string}]}.\n";

    public async Task<ResaleEstimate?> EstimateAsync(
        string productDescription,
        ResaleSearchHints? searchHints,
        CancellationToken cancellationToken) =>
        (await SearchAsync(productDescription, searchHints, cancellationToken)).Estimate;

    public async Task<ResaleSearchOutcome> SearchAsync(
        string productDescription,
        ResaleSearchHints? searchHints,
        CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(productDescription))
        {
            return ResaleSearchOutcome.NothingFound;
        }

        string cacheKey = $"resale:{Model}:{NormalizeKey(productDescription)}";
        if (cache.TryGetValue(cacheKey, out ResaleSearchOutcome? cached) && cached is not null)
        {
            return cached;
        }

        ResaleSearchOutcome outcome = await FetchAsync(productDescription, searchHints, apiKey, cancellationToken);

        // Failures are not cached: the next try should actually try.
        if (outcome.Failure is null)
        {
            cache.Set(cacheKey, outcome, outcome.Estimate is not null ? FoundTtl : NothingFoundTtl);
        }

        return outcome;
    }

    // Keeps every distinguishing token (colorway, year, size, edition), so different variants never
    // share an entry; only identical descriptions collapse.
    private static string NormalizeKey(string description)
    {
        StringBuilder sb = new(description.Length);
        bool previousSpace = false;
        foreach (char ch in description.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                previousSpace = false;
            }
            else if (sb.Length > 0 && !previousSpace)
            {
                sb.Append(' ');
                previousSpace = true;
            }
        }

        string key = sb.ToString().Trim();
        return key.Length > 160 ? key[..160] : key;
    }

    private async Task<ResaleSearchOutcome> FetchAsync(
        string productDescription,
        ResaleSearchHints? searchHints,
        string apiKey,
        CancellationToken cancellationToken)
    {
        // The google_search tool cannot be combined with responseSchema, so the JSON shape is asked
        // for in the prompt and read out of the grounded text.
        string json = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new object[] { new { text = BuildPrompt(productDescription, searchHints) } } } },
            tools = new object[] { new { google_search = new { } } },
            generationConfig = new { temperature = 0.2 },
        });
        Uri endpoint = new($"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ResaleSearchOutcome.Failed("Web search took too long.");
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Grounded resale search could not reach Gemini (attempt {Attempt})", attempt);
                if (attempt < MaxAttempts)
                {
                    await Backoff(attempt, cancellationToken);
                    continue;
                }

                return ResaleSearchOutcome.Failed("Web search could not be reached.");
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ResaleEstimate? estimate = Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    return estimate is null ? ResaleSearchOutcome.NothingFound : ResaleSearchOutcome.Found(estimate);
                }

                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Grounded resale search returned {Status} (attempt {Attempt}): {Body}",
                    response.StatusCode, attempt, body[..Math.Min(body.Length, 300)]);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && body.Contains("PerDay", StringComparison.OrdinalIgnoreCase))
                {
                    return ResaleSearchOutcome.Failed("Web search has used today's quota.");
                }

                bool transient = response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.InternalServerError;
                if (transient && attempt < MaxAttempts)
                {
                    await Backoff(attempt, cancellationToken);
                    continue;
                }

                return ResaleSearchOutcome.Failed(transient ? "Web search is busy. Try again shortly." : "Web search returned an error.");
            }
        }

        return ResaleSearchOutcome.Failed("Web search is busy. Try again shortly.");
    }

    private static Task Backoff(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))), cancellationToken);

    private static string BuildPrompt(string productDescription, ResaleSearchHints? searchHints)
    {
        StringBuilder prompt = new(Prompt);
        prompt.Append("\nPRODUCT FACTS:\n").Append(productDescription.Trim()).Append('\n');
        if (!string.IsNullOrWhiteSpace(searchHints?.SearchQuery))
        {
            prompt.Append("\nSearch eBay sold listings for: ").Append(searchHints.SearchQuery.Trim()).Append('\n');
        }

        return prompt.ToString();
    }

    private ResaleEstimate? Parse(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("candidates", out JsonElement candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement candidate = candidates[0];
            StringBuilder text = new();
            if (candidate.TryGetProperty("content", out JsonElement content)
                && content.TryGetProperty("parts", out JsonElement parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    {
                        text.Append(value.GetString());
                    }
                }
            }

            string? jsonText = ExtractJsonObject(text.ToString());
            if (jsonText is null)
            {
                return null;
            }

            EstimateDto? dto = JsonSerializer.Deserialize<EstimateDto>(jsonText, JsonOptions);
            if (dto is null || dto.High <= 0)
            {
                return null;
            }

            decimal low = Math.Max(0, dto.Low);
            return new ResaleEstimate(
                Low: decimal.Round(low, 2),
                High: decimal.Round(Math.Max(low, dto.High), 2),
                Rationale: dto.Rationale?.Trim(),
                ValuationConfidence: (dto.ValuationConfidence ?? "low").ToLowerInvariant() switch
                {
                    "high" => "high",
                    "medium" => "medium",
                    _ => "low",
                },
                PriceRangeType: (dto.PriceRangeType ?? "").ToLowerInvariant() switch
                {
                    "tight" => "tight",
                    "wide" => "wide",
                    _ => "moderate",
                },
                KeyValueFactors: CleanList(dto.KeyValueFactors, 6),
                MissingInformation: CleanList(dto.MissingInformation, 6),
                RecommendedNextSteps: CleanList(dto.RecommendedNextSteps, 5),
                Comps: CleanComps(dto.Comps, clock.GetUtcNow()),
                Sources: ExtractSources(candidate));
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Grounded resale search answer was not valid JSON");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // The first balanced JSON object in grounded text, which may sit inside ```json fences or prose.
    private static string? ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            char ch = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
            }
            else if (ch == '"') inString = true;
            else if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0) return text[start..(i + 1)];
        }

        return null;
    }

    private static IReadOnlyList<ResaleSource> ExtractSources(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("groundingMetadata", out JsonElement metadata)
            || !metadata.TryGetProperty("groundingChunks", out JsonElement chunks)
            || chunks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<ResaleSource> sources = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement chunk in chunks.EnumerateArray().Take(8))
        {
            if (!chunk.TryGetProperty("web", out JsonElement web)) continue;
            string? uri = web.TryGetProperty("uri", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(uri) || !seen.Add(uri)) continue;
            string? title = web.TryGetProperty("title", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            sources.Add(new ResaleSource(uri, title));
        }

        return sources;
    }

    private static IReadOnlyList<string> CleanList(List<string>? items, int max) =>
        (items ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Take(max).ToArray();

    private static IReadOnlyList<ResaleComp> CleanComps(List<CompDto>? comps, DateTimeOffset now) =>
        (comps ?? [])
            .Where(comp => comp.Price > 0)
            .Select(comp => new ResaleComp(
                Price: decimal.Round(comp.Price, 2),
                Status: string.Equals(comp.Status, "active", StringComparison.OrdinalIgnoreCase) ? "active" : "sold",
                Condition: Clean(comp.Condition),
                Note: Clean(comp.Note),
                SourceUrl: comp.SourceUrl is { Length: > 0 } url && Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? parsed)
                    && parsed.Scheme is "http" or "https" ? parsed.ToString() : null,
                Title: Clean(comp.Title),
                Date: ParseDate(comp.Date, now)))
            .Take(8)
            .ToArray();

    // A date the model cannot have seen (in the future, or unreadable) is dropped rather than trusted.
    private static DateTimeOffset? ParseDate(string? value, DateTimeOffset now) =>
        DateTimeOffset.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTimeOffset date) && date <= now
            ? date
            : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class EstimateDto
    {
        [JsonPropertyName("low")] public decimal Low { get; set; }
        [JsonPropertyName("high")] public decimal High { get; set; }
        [JsonPropertyName("rationale")] public string? Rationale { get; set; }
        [JsonPropertyName("priceRangeType")] public string? PriceRangeType { get; set; }
        [JsonPropertyName("valuationConfidence")] public string? ValuationConfidence { get; set; }
        [JsonPropertyName("keyValueFactors")] public List<string>? KeyValueFactors { get; set; }
        [JsonPropertyName("missingInformation")] public List<string>? MissingInformation { get; set; }
        [JsonPropertyName("recommendedNextSteps")] public List<string>? RecommendedNextSteps { get; set; }
        [JsonPropertyName("comps")] public List<CompDto>? Comps { get; set; }
    }

    private sealed class CompDto
    {
        [JsonPropertyName("price")] public decimal Price { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("condition")] public string? Condition { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; set; }
    }
}
