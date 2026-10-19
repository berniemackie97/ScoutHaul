using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Vision;

/// <summary>
/// Generates ready-to-post marketplace listings with Gemini. Pulls real sold/active comps + a used
/// resale range from <see cref="IResaleEstimator"/> (grounded Google Search, cached), then writes an
/// optimized title and honest description. The final suggested list price is deterministic and comes
/// from <see cref="ListingPriceAdvisor"/>, not from the copy model.
/// </summary>
internal sealed class GeminiListingWriter(
    HttpClient httpClient,
    IConfiguration configuration,
    IResaleEstimator resaleEstimator,
    ILogger<GeminiListingWriter> logger) : IListingGenerationService
{
    private string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]);

    public async Task<GeneratedListingDto?> GenerateAsync(GenerateListingRequest request, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(request.Title))
        {
            return null;
        }

        string details = BuildDetails(request);
        ResaleSearchHints searchHints = ResaleSearchHintBuilder.FromListingRequest(request);

        // Real sold/active comps + a used resale range (grounded, cached) drive the suggested price.
        ResaleEstimate? estimate = await resaleEstimator.EstimateAsync(details, searchHints, cancellationToken);

        ListingPriceAdvice priceAdvice = ListingPriceAdvisor.Calculate(request, estimate);

        ListingCopyDto? copy = await GenerateCopyAsync(details, estimate, priceAdvice, apiKey, cancellationToken);
        if (copy is null || string.IsNullOrWhiteSpace(copy.Title))
        {
            return null;
        }

        return new GeneratedListingDto(
            ListingTitle: copy.Title!.Trim(),
            ListingDescription: (copy.Description ?? string.Empty).Trim(),
            SuggestedListPrice: priceAdvice.SuggestedListPrice,
            EstimatedResaleLow: estimate?.Low,
            EstimatedResaleHigh: estimate?.High,
            PriceRationale: priceAdvice.PriceRationale ?? Clean(copy.PriceRationale) ?? estimate?.Rationale,
            Comps: priceAdvice.PricingComps.Count > 0 ? priceAdvice.PricingComps : estimate?.Comps ?? []);
    }

    private static string BuildDetails(GenerateListingRequest r)
    {
        List<string> lines = [r.Title.Trim()];
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {value.Trim()}");
        }
        Add("Brand", r.Brand);
        Add("Model", r.Model);
        Add("Category", r.Category);
        Add("Condition", r.Condition);
        Add("Color", r.Color);
        string size = string.Join(" ", new[] { r.SizeSystem, r.SizeValue }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        if (size.Length > 0) lines.Add($"Size: {size}");
        Add("Structured attributes JSON", r.AttributesJson);
        Add("Notes", r.Notes);
        return string.Join("\n", lines);
    }

    private const string SystemPrompt =
        "You are an expert reseller who writes high-converting secondhand marketplace listings (eBay, " +
        "Poshmark, Mercari, Facebook Marketplace). From the item details — and the real comps provided — " +
        "produce TWO things:\n" +
        "1) title: a keyword-rich marketplace title, MAX 80 characters. Front-load brand + model + the most " +
        "searched attributes (type, size, color, edition/year). No ALL-CAPS spam, no emojis, no fabricated " +
        "specs. For trading cards, include TCG/franchise, card name, exact card number, set, variant/rarity, " +
        "and condition when known.\n" +
        "2) description: an honest, scannable description a buyer trusts — what it is, key features/specs, " +
        "condition (state wear/flaws plainly), what's included, and a short reason to buy. Use concise buyer-" +
        "friendly bullets when helpful. Avoid generic openers like \"This listing is for\". Never invent " +
        "attributes, measurements, authenticity, or completeness that are not in the details.\n" +
        "Pricing is handled by the app. Do NOT choose or mention a list price in the title/description.\n" +
        "Respond ONLY with valid JSON matching the schema.";

    private async Task<ListingCopyDto?> GenerateCopyAsync(
        string details,
        ResaleEstimate? estimate,
        ListingPriceAdvice priceAdvice,
        string apiKey,
        CancellationToken cancellationToken)
    {
        StringBuilder context = new();
        context.Append("ITEM DETAILS:\n").Append(details).Append("\n\n");
        if (estimate is not null)
        {
            context.Append($"USED RESALE RANGE: ${estimate.Low:0.##}–${estimate.High:0.##} ({estimate.PriceRangeType}).\n");
            if (estimate.Comps is { Count: > 0 } comps)
            {
                context.Append("REAL COMPS:\n");
                foreach (ResaleComp c in comps.Take(8))
                {
                    string cond = c.Condition is { Length: > 0 } x ? $" {x}" : string.Empty;
                    string note = c.Note is { Length: > 0 } n ? $" — {n}" : string.Empty;
                    context.Append($"- ${c.Price:0.##} [{c.Status}]{cond}{note}\n");
                }
            }
        }
        if (priceAdvice.SuggestedListPrice is { } suggestedPrice)
        {
            context.Append("\nAPP-CALCULATED LIST PRICE: $")
                .Append(suggestedPrice.ToString("0.##"))
                .Append(". Use it only as market context; do not include the price in copy.\n");
        }

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = SystemPrompt }, new { text = context.ToString() } } },
            },
            generationConfig = new
            {
                temperature = 0.5, // some voice for the copy, but anchored to the facts + comps
                responseMimeType = "application/json",
                responseSchema = ResponseSchema,
            },
        };

        Uri endpoint = new($"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent");
        string json = JsonSerializer.Serialize(requestBody);

        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
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
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gemini listing request threw (attempt {Attempt}/{Max})", attempt, maxAttempts);
                if (attempt < maxAttempts) { await Backoff(attempt, cancellationToken); continue; }
                return null;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                }

                bool transient = response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.ServiceUnavailable
                    or System.Net.HttpStatusCode.InternalServerError;
                if (transient && attempt < maxAttempts)
                {
                    await Backoff(attempt, cancellationToken);
                    continue;
                }
                return null;
            }
        }

        return null;
    }

    private static Task Backoff(int attempt, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromMilliseconds(400 * (1 << (attempt - 1))), ct);

    private ListingCopyDto? Parse(string payload)
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
            JsonElement first = candidates[0];
            if (!first.TryGetProperty("content", out JsonElement contentEl)
                || !contentEl.TryGetProperty("parts", out JsonElement parts)
                || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
            {
                return null;
            }
            string? jsonText = parts[0].TryGetProperty("text", out JsonElement t) ? t.GetString() : null;
            return string.IsNullOrWhiteSpace(jsonText) ? null : JsonSerializer.Deserialize<ListingCopyDto>(jsonText, JsonOpts);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Gemini listing response");
            return null;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "Marketplace title, max 80 chars, keyword-rich" },
            description = new { type = "string", description = "Honest scannable listing description" },
            priceRationale = new { type = "string", description = "Optional one-line market context, without choosing a price" },
        },
        required = new[] { "title", "description" },
    };

    private sealed class ListingCopyDto
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("listPrice")] public decimal ListPrice { get; set; }
        [JsonPropertyName("priceRationale")] public string? PriceRationale { get; set; }
    }
}
