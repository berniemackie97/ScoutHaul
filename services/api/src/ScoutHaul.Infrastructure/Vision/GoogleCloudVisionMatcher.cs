using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Vision;

/// <summary>
/// Reverse-image web matching via Google Cloud Vision WEB_DETECTION.
/// Supplies real product/marketplace page URLs found across the web — the one thing
/// the LLM identifier cannot do (it has no live web access). Optional: only runs if configured.
/// </summary>
internal sealed class GoogleCloudVisionMatcher(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GoogleCloudVisionMatcher> logger) : IWebImageMatcher
{
    private const string VisionApiUrl = "https://vision.googleapis.com/v1/images:annotate";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["GoogleCloudVision:ApiKey"]);

    public async Task<WebMatchResult> MatchAsync(string imageBase64, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["GoogleCloudVision:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return WebMatchResult.Empty;
        }

        var requestBody = new
        {
            requests = new[]
            {
                new
                {
                    image = new { content = imageBase64 },
                    features = new object[]
                    {
                        new { type = "WEB_DETECTION", maxResults = 10 },
                        new { type = "LABEL_DETECTION", maxResults = 10 },
                        new { type = "LOGO_DETECTION", maxResults = 5 },
                    },
                },
            },
        };

        string json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        string url = $"{VisionApiUrl}?key={apiKey}";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(url, content, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google Cloud Vision request failed");
            return WebMatchResult.Empty;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Google Cloud Vision returned {StatusCode}: {Body}",
                    response.StatusCode, errorBody[..Math.Min(errorBody.Length, 500)]);
                return WebMatchResult.Empty;
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse Google Cloud Vision response as JSON");
                return WebMatchResult.Empty;
            }
            using (doc)
            {
                return ParseResponse(doc);
            }
        }
    }

    private static WebMatchResult ParseResponse(JsonDocument doc)
    {
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("responses", out JsonElement responses)
            || responses.ValueKind != JsonValueKind.Array
            || responses.GetArrayLength() == 0)
        {
            return WebMatchResult.Empty;
        }

        JsonElement r = responses[0];
        string? bestGuessLabel = null;
        List<VisionWebEntity> webEntities = [];
        List<VisionMatchingPage> matchingPages = [];
        List<VisionSimilarImage> similarImages = [];

        if (r.TryGetProperty("webDetection", out JsonElement web))
        {
            if (web.TryGetProperty("bestGuessLabels", out JsonElement bgls) && bgls.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement label in bgls.EnumerateArray())
                {
                    bestGuessLabel ??= GetString(label, "label");
                }
            }

            if (web.TryGetProperty("webEntities", out JsonElement entities) && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entity in entities.EnumerateArray().Take(10))
                {
                    string? desc = GetString(entity, "description");
                    if (string.IsNullOrWhiteSpace(desc)) continue;
                    double score = entity.TryGetProperty("score", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0;
                    webEntities.Add(new VisionWebEntity(desc, Math.Round(score, 3)));
                }
            }

            if (web.TryGetProperty("pagesWithMatchingImages", out JsonElement pages) && pages.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement page in pages.EnumerateArray().Take(8))
                {
                    string? pageUrl = GetString(page, "url");
                    if (string.IsNullOrWhiteSpace(pageUrl)) continue;
                    matchingPages.Add(new VisionMatchingPage(pageUrl, GetString(page, "pageTitle")));
                }
            }

            if (web.TryGetProperty("visuallySimilarImages", out JsonElement similar) && similar.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement img in similar.EnumerateArray().Take(6))
                {
                    string? imgUrl = GetString(img, "url");
                    if (!string.IsNullOrWhiteSpace(imgUrl)) similarImages.Add(new VisionSimilarImage(imgUrl));
                }
            }
        }

        List<string> labels = [];
        if (r.TryGetProperty("labelAnnotations", out JsonElement labelAnnotations) && labelAnnotations.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in labelAnnotations.EnumerateArray().Take(10))
            {
                string? desc = GetString(a, "description");
                if (!string.IsNullOrWhiteSpace(desc)) labels.Add(desc);
            }
        }

        List<string> logos = [];
        if (r.TryGetProperty("logoAnnotations", out JsonElement logoAnnotations) && logoAnnotations.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in logoAnnotations.EnumerateArray().Take(5))
            {
                string? desc = GetString(a, "description");
                if (!string.IsNullOrWhiteSpace(desc)) logos.Add(desc);
            }
        }

        return new WebMatchResult(bestGuessLabel, webEntities, matchingPages, similarImages, labels, logos, DetectedText: null);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
