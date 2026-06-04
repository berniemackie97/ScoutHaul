using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Vision;

/// <summary>
/// Text-only resale estimator backed by Gemini. Given a product name/brand (e.g. resolved from a
/// barcode), returns a realistic USED resale price range. Reuses the same free Gemini key as the
/// vision identifier, so barcode scans get a value estimate with no paid pricing provider.
/// </summary>
internal sealed class GeminiResaleEstimator(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiResaleEstimator> logger) : IResaleEstimator
{
    private string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]);

    private const string Prompt =
        "You are an expert reseller appraiser. Estimate the realistic USED/secondhand resale price " +
        "range in USD for the following product on the resale market (eBay sold listings / Facebook " +
        "Marketplace), NOT retail price. If the product is vague or unrecognizable, give your best " +
        "general estimate. Keep the range tight and realistic. Provide a one-sentence rationale. " +
        "Respond ONLY with JSON matching the schema. Product: ";

    public async Task<ResaleEstimate?> EstimateAsync(string productDescription, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(productDescription))
        {
            return null;
        }

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
