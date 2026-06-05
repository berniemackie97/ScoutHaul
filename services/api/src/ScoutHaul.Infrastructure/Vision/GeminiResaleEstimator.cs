using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Vision;

/// <summary>
/// Text-only resale estimator backed by Gemini. Given a product name/brand (e.g. resolved from a
/// barcode), returns a realistic USED resale price range. Reuses the same free Gemini key as the
/// vision identifier, so barcode scans get a value estimate with no paid pricing provider.
///
/// Results are cached by normalized product description — the same item scanned repeatedly does
/// not burn the shared free-tier quota, and repeat scans return instantly.
/// </summary>
internal sealed class GeminiResaleEstimator(
    HttpClient httpClient,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<GeminiResaleEstimator> logger) : IResaleEstimator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]);

    private const string Prompt =
        "You are an expert reseller appraiser. Use Google Search to find REAL, RECENT resale comps for the " +
        "product below, then estimate its USED/secondhand resale value in USD — NEVER retail, MSRP, or " +
        "optimistic asking price.\n" +
        "Search eBay SOLD/completed listings first (the gold standard), then active eBay/Facebook Marketplace/" +
        "Mercari/Poshmark/StockX. Use the ACTUAL prices you find. low = quick-sale / lower-condition value; " +
        "high = realistic private-party value in good used condition (not a rare outlier). Assume typical used " +
        "condition unless stated. Be CONSERVATIVE; price damaged/incomplete/untested/counterfeit-risk LOWER. " +
        "Generic or variant-heavy items get a WIDE range unless the exact variant is clear (price_range_type " +
        "must match). Set valuation_confidence by how good the comps are.\n" +
        "comps: list the 3-8 REAL listings you actually found — each with its price, status (\"sold\" or " +
        "\"active\"), condition, a short note, and the source URL if available. Do NOT fabricate prices; only " +
        "report what the search returned.\n" +
        "Respond with ONLY a JSON object (no prose, no markdown fences) of shape: " +
        "{\"low\":number,\"high\":number,\"priceRangeType\":\"tight|moderate|wide\"," +
        "\"valuationConfidence\":\"high|medium|low\",\"keyValueFactors\":[string]," +
        "\"missingInformation\":[string],\"recommendedNextSteps\":[string],\"rationale\":string," +
        "\"comps\":[{\"price\":number,\"status\":\"sold|active\",\"condition\":string,\"note\":string,\"sourceUrl\":string}]}.\n" +
        "Product: ";

    public async Task<ResaleEstimate?> EstimateAsync(string productDescription, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(productDescription))
        {
            return null;
        }

        // Cache by normalized description — same product → same estimate, no repeat API call.
        string cacheKey = $"resale:{Model}:{NormalizeKey(productDescription)}";
        if (cache.TryGetValue(cacheKey, out ResaleEstimate? cached))
        {
            logger.LogDebug("Resale estimate cache hit for {Product}", productDescription);
            return cached;
        }

        ResaleEstimate? estimate = await FetchEstimateAsync(productDescription, apiKey, cancellationToken);

        // Cache successes (24h) and misses (10m, so a transient failure isn't stuck for a day).
        cache.Set(cacheKey, estimate, estimate is not null ? CacheTtl : TimeSpan.FromMinutes(10));
        return estimate;
    }

    // Cache key = every distinguishing token in the description, lowercased, punctuation-stripped.
    // This deliberately KEEPS variant tokens (colorway, year, size, edition) so different-value
    // variants — "air jordan 1 chicago" vs "air jordan 1 bred" — never share a cache entry.
    // Only truly identical descriptions collapse to the same key (same question → same answer).
    private static string NormalizeKey(string description)
    {
        var sb = new StringBuilder(description.Length);
        bool prevSpace = false;
        foreach (char ch in description.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                prevSpace = false;
            }
            else if (sb.Length > 0 && !prevSpace)
            {
                sb.Append(' ');
                prevSpace = true;
            }
        }
        string key = sb.ToString().Trim();
        return key.Length > 160 ? key[..160] : key;
    }

    private async Task<ResaleEstimate?> FetchEstimateAsync(string productDescription, string apiKey, CancellationToken cancellationToken)
    {
        // Grounded with Google Search → REAL recent sold/active listings, not training-knowledge
        // guesses. Note: the google_search tool cannot be combined with responseSchema, so we
        // instruct JSON in the prompt and parse it from the grounded text, plus capture the real
        // source URLs from groundingMetadata.
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = Prompt + productDescription.Trim() } } },
            },
            tools = new object[] { new { google_search = new { } } },
            generationConfig = new { temperature = 0.2 },
        };

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={apiKey}";
        string json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(url, content, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini resale estimate request failed for {Product}", productDescription);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(payload);
        }
    }

    private ResaleEstimate? Parse(string payload)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("candidates", out JsonElement candidates)
                || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement candidate = candidates[0];

            // Concatenate all text parts (grounded responses can split across parts).
            var sb = new StringBuilder();
            if (candidate.TryGetProperty("content", out JsonElement contentEl)
                && contentEl.TryGetProperty("parts", out JsonElement parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement p in parts.EnumerateArray())
                {
                    if (p.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
                }
            }

            // Grounding (Google Search) cannot use responseSchema, so the JSON is embedded in text,
            // sometimes inside ```json fences or with stray prose. Extract the JSON object.
            string? jsonText = ExtractJsonObject(sb.ToString());
            if (string.IsNullOrWhiteSpace(jsonText)) return null;

            EstimateDto? dto = JsonSerializer.Deserialize<EstimateDto>(jsonText, JsonOpts);
            if (dto is null || dto.High <= 0) return null;

            decimal low = Math.Max(0, dto.Low);
            decimal high = Math.Max(low, dto.High);
            string rangeType = (dto.PriceRangeType ?? "").ToLowerInvariant() switch
            {
                "tight" => "tight",
                "wide" => "wide",
                _ => "moderate",
            };
            string confidence = (dto.ValuationConfidence ?? "low").ToLowerInvariant() switch
            {
                "high" => "high",
                "medium" => "medium",
                _ => "low",
            };

            return new ResaleEstimate(
                Low: decimal.Round(low, 2),
                High: decimal.Round(high, 2),
                Rationale: dto.Rationale?.Trim(),
                ValuationConfidence: confidence,
                PriceRangeType: rangeType,
                KeyValueFactors: CleanList(dto.KeyValueFactors, 6),
                MissingInformation: CleanList(dto.MissingInformation, 6),
                RecommendedNextSteps: CleanList(dto.RecommendedNextSteps, 5),
                Comps: CleanComps(dto.Comps),
                Sources: ExtractSources(candidate));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Gemini resale estimate");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Pull the first balanced JSON object out of grounded text (handles ```json fences + prose).
    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int start = text.IndexOf('{');
        if (start < 0) return null;
        int depth = 0;
        bool inStr = false, esc = false;
        for (int i = start; i < text.Length; i++)
        {
            char ch = text[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (ch == '\\') esc = true;
                else if (ch == '"') inStr = false;
            }
            else if (ch == '"') inStr = true;
            else if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0) return text[start..(i + 1)];
        }
        return null;
    }

    // Real source links the grounded search used (groundingMetadata.groundingChunks[].web).
    private static IReadOnlyList<ResaleSource> ExtractSources(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("groundingMetadata", out JsonElement gm)
            || !gm.TryGetProperty("groundingChunks", out JsonElement chunks)
            || chunks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<ResaleSource> sources = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        (items ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Take(max)
            .ToArray();

    internal static IReadOnlyList<ResaleComp> CleanComps(List<CompDto>? comps) =>
        (comps ?? [])
            .Where(c => c.Price > 0)
            .Select(c => new ResaleComp(
                Price: decimal.Round(c.Price, 2),
                Status: string.Equals(c.Status, "active", StringComparison.OrdinalIgnoreCase) ? "active" : "sold",
                Condition: string.IsNullOrWhiteSpace(c.Condition) ? null : c.Condition.Trim(),
                Note: string.IsNullOrWhiteSpace(c.Note) ? null : c.Note.Trim(),
                SourceUrl: c.SourceUrl is { Length: > 0 } su && su.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? su.Trim() : null))
            .Take(8)
            .ToArray();

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

    internal sealed class CompDto
    {
        [JsonPropertyName("price")] public decimal Price { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("condition")] public string? Condition { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; set; }
    }
}
