using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Vision;

/// <summary>
/// Identifies products from photos using Gemini multimodal (free tier: 1,500 req/day).
/// Unlike reverse-image search, the model reasons about the item and returns a specific
/// product name, brand, model, category, and resale value estimate as structured JSON.
/// </summary>
internal sealed class GeminiProductVisionService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiProductVisionService> logger) : IProductIdentifier
{
    // Free-tier model. Configurable so it can be bumped without a code change.
    private string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]);

    private const string SystemPrompt =
        "You are an expert reseller and product appraiser identifying secondhand items for resale " +
        "(thrift, estate, garage sale sourcing). Examine the image and identify the single main product. " +
        "Be as SPECIFIC as possible: prefer \"Sony WH-1000XM4 Wireless Headphones\" over \"headphones\", " +
        "\"Pyrex 4-Cup Glass Measuring Cup\" over \"measuring cup\". Read any visible text, logos, model numbers, " +
        "or tags to pin down the exact product. If you genuinely cannot tell the specific product, give the most " +
        "specific category you can and set confidence to \"low\". " +
        "Estimate a realistic USED resale price range in USD for the secondhand market (eBay sold / Facebook Marketplace), " +
        "not retail. Provide 3-6 search keyword phrases a reseller would type to find comps. " +
        "Respond ONLY with the JSON object matching the schema.";

    public async Task<ProductIdentification?> IdentifyAsync(string imageBase64, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Gemini API key is not configured");
            return null;
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = SystemPrompt },
                        new { inline_data = new { mime_type = "image/jpeg", data = imageBase64 } },
                    },
                },
            },
            generationConfig = new
            {
                temperature = 0.1, // deterministic — we want facts, not creativity
                responseMimeType = "application/json",
                responseSchema = ResponseSchema,
            },
        };

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={apiKey}";
        string json = JsonSerializer.Serialize(requestBody);

        // Free tier is 10 RPM — a transient 429/503 is common under burst. Retry once with a
        // short backoff before giving up, so a momentary throttle doesn't surface as failure.
        const int maxAttempts = 2;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(url, content, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gemini vision request failed (attempt {Attempt})", attempt);
                return null;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    string payload = await response.Content.ReadAsStringAsync(cancellationToken);
                    return ParseResponse(payload);
                }

                bool transient = response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.ServiceUnavailable
                    or System.Net.HttpStatusCode.InternalServerError;

                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Gemini returned {StatusCode} (attempt {Attempt}/{Max}): {Body}",
                    response.StatusCode, attempt, maxAttempts, body[..Math.Min(body.Length, 300)]);

                if (transient && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
                    continue;
                }
                return null;
            }
        }

        return null;
    }

    private ProductIdentification? ParseResponse(string payload)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;

            // Navigate candidates[0].content.parts[0].text → which is itself JSON
            if (!root.TryGetProperty("candidates", out JsonElement candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                logger.LogWarning("Gemini response had no candidates");
                return null;
            }

            JsonElement first = candidates[0];
            if (!first.TryGetProperty("content", out JsonElement contentEl)
                || !contentEl.TryGetProperty("parts", out JsonElement parts)
                || parts.ValueKind != JsonValueKind.Array
                || parts.GetArrayLength() == 0)
            {
                return null;
            }

            string? jsonText = parts[0].TryGetProperty("text", out JsonElement textEl) ? textEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return null;
            }

            GeminiProductDto? dto = JsonSerializer.Deserialize<GeminiProductDto>(jsonText, JsonOpts);
            if (dto is null)
            {
                return null;
            }

            string confidence = (dto.Confidence ?? "low").ToLowerInvariant() switch
            {
                "high" => "high",
                "medium" => "medium",
                _ => "low",
            };

            return new ProductIdentification(
                Name: Clean(dto.Name),
                Brand: Clean(dto.Brand),
                Model: Clean(dto.Model),
                Category: Clean(dto.Category),
                Color: Clean(dto.Color),
                Condition: Clean(dto.Condition),
                EstimatedResaleLow: dto.EstimatedResaleLow > 0 ? dto.EstimatedResaleLow : null,
                EstimatedResaleHigh: dto.EstimatedResaleHigh > 0 ? dto.EstimatedResaleHigh : null,
                IdentifyingText: Clean(dto.IdentifyingText),
                SearchKeywords: (dto.SearchKeywords ?? [])
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => k.Trim())
                    .Take(6)
                    .ToArray(),
                Confidence: confidence);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Gemini JSON response");
            return null;
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string trimmed = value.Trim();
        // Model sometimes emits "unknown"/"n/a" for missing fields — normalize to null
        return trimmed.ToLowerInvariant() is "unknown" or "n/a" or "none" or "not visible" or "unidentified"
            ? null
            : trimmed;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Gemini structured-output schema (OpenAPI subset).
    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "Specific product name" },
            brand = new { type = "string" },
            model = new { type = "string" },
            category = new { type = "string" },
            color = new { type = "string" },
            condition = new { type = "string", description = "Apparent condition from the photo" },
            estimatedResaleLow = new { type = "number", description = "Low end of used resale price in USD" },
            estimatedResaleHigh = new { type = "number", description = "High end of used resale price in USD" },
            identifyingText = new { type = "string", description = "Any text/model numbers visible on the item" },
            searchKeywords = new { type = "array", items = new { type = "string" } },
            confidence = new { type = "string", @enum = new[] { "high", "medium", "low" } },
        },
        required = new[] { "name", "category", "confidence", "searchKeywords" },
    };

    private sealed class GeminiProductDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("color")] public string? Color { get; set; }
        [JsonPropertyName("condition")] public string? Condition { get; set; }
        [JsonPropertyName("estimatedResaleLow")] public decimal EstimatedResaleLow { get; set; }
        [JsonPropertyName("estimatedResaleHigh")] public decimal EstimatedResaleHigh { get; set; }
        [JsonPropertyName("identifyingText")] public string? IdentifyingText { get; set; }
        [JsonPropertyName("searchKeywords")] public List<string>? SearchKeywords { get; set; }
        [JsonPropertyName("confidence")] public string? Confidence { get; set; }
    }
}
