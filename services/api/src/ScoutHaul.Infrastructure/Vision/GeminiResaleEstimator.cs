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
        "You are an expert reseller appraiser. Estimate the realistic USED/secondhand resale price " +
        "range in USD on the resale market (eBay sold listings, Facebook Marketplace) — NOT retail.\n" +
        "CRITICAL: Many products have variants (year, colorway, edition, model revision, size, " +
        "generation) and conditions that drastically change value. " +
        "If the description pins down the EXACT variant, give a tight, accurate range. " +
        "If it is GENERIC or could refer to multiple variants with very different values " +
        "(e.g. 'Nike Air Jordan 1', 'Pokemon card', 'iPhone', 'Levi's jeans'), return a WIDER range " +
        "that spans the common variants, and say in the rationale that value depends on the specific " +
        "variant/condition. Assume typical used condition unless stated, and let the range reflect " +
        "condition variance. The rationale must name the key value factors (variant, year, condition). " +
        "Respond ONLY with JSON matching the schema. Product: ";

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
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = Prompt + productDescription.Trim() } } },
            },
            generationConfig = new
            {
                temperature = 0.2,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        low = new { type = "number" },
                        high = new { type = "number" },
                        rationale = new { type = "string" },
                    },
                    required = new[] { "low", "high" },
                },
            },
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

            string? jsonText = candidates[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(jsonText)) return null;

            EstimateDto? dto = JsonSerializer.Deserialize<EstimateDto>(jsonText);
            if (dto is null || dto.High <= 0) return null;

            decimal low = Math.Max(0, dto.Low);
            decimal high = Math.Max(low, dto.High);
            return new ResaleEstimate(decimal.Round(low, 2), decimal.Round(high, 2), dto.Rationale?.Trim());
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Gemini resale estimate");
            return null;
        }
    }

    private sealed class EstimateDto
    {
        [JsonPropertyName("low")] public decimal Low { get; set; }
        [JsonPropertyName("high")] public decimal High { get; set; }
        [JsonPropertyName("rationale")] public string? Rationale { get; set; }
    }
}
