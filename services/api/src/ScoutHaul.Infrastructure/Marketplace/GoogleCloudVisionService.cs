using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed class GoogleCloudVisionService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GoogleCloudVisionService> logger) : IVisionIdentificationService
{
    private const string VisionApiUrl = "https://vision.googleapis.com/v1/images:annotate";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["GoogleCloudVision:ApiKey"]);

    public async Task<VisionIdentifyResult> IdentifyAsync(string imageBase64, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["GoogleCloudVision:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Google Cloud Vision API key is not configured");
            return EmptyResult();
        }

        // Build Vision API request — Web Detection + Label Detection
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
                        new { type = "LABEL_DETECTION", maxResults = 15 },
                    }
                }
            }
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
            logger.LogError(ex, "Google Cloud Vision API request failed");
            return EmptyResult();
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Google Cloud Vision returned {StatusCode}: {Body}",
                response.StatusCode, errorBody[..Math.Min(errorBody.Length, 500)]);
            return EmptyResult();
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return ParseResponse(doc);
    }

    private VisionIdentifyResult ParseResponse(JsonDocument doc)
    {
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("responses", out JsonElement responses)
            || responses.ValueKind != JsonValueKind.Array
            || responses.GetArrayLength() == 0)
        {
            return EmptyResult();
        }

        JsonElement firstResponse = responses[0];

        // Parse Web Detection
        string? bestGuessLabel = null;
        List<VisionWebEntity> webEntities = [];
        List<VisionMatchingPage> matchingPages = [];
        List<VisionSimilarImage> similarImages = [];

        if (firstResponse.TryGetProperty("webDetection", out JsonElement webDetection))
        {
            // Best guess labels
            if (webDetection.TryGetProperty("bestGuessLabels", out JsonElement bestGuessLabels)
                && bestGuessLabels.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement label in bestGuessLabels.EnumerateArray())
                {
                    bestGuessLabel ??= GetString(label, "label");
                }
            }

            // Web entities — product names, brands, categories from the web
            if (webDetection.TryGetProperty("webEntities", out JsonElement entities)
                && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entity in entities.EnumerateArray().Take(10))
                {
                    string? desc = GetString(entity, "description");
                    if (string.IsNullOrWhiteSpace(desc)) continue;

                    double score = entity.TryGetProperty("score", out JsonElement scoreEl)
                        && scoreEl.ValueKind == JsonValueKind.Number
                        ? scoreEl.GetDouble()
                        : 0;

                    webEntities.Add(new VisionWebEntity(desc, Math.Round(score, 3)));
                }
            }

            // Pages with matching images — marketplace listings, product pages
            if (webDetection.TryGetProperty("pagesWithMatchingImages", out JsonElement pages)
                && pages.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement page in pages.EnumerateArray().Take(8))
                {
                    string? pageUrl = GetString(page, "url");
                    string? pageTitle = GetString(page, "pageTitle");
                    if (string.IsNullOrWhiteSpace(pageUrl)) continue;

                    matchingPages.Add(new VisionMatchingPage(pageUrl, pageTitle));
                }
            }

            // Visually similar images
            if (webDetection.TryGetProperty("visuallySimilarImages", out JsonElement similar)
                && similar.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement img in similar.EnumerateArray().Take(6))
                {
                    string? imgUrl = GetString(img, "url");
                    if (!string.IsNullOrWhiteSpace(imgUrl))
                    {
                        similarImages.Add(new VisionSimilarImage(imgUrl));
                    }
                }
            }
        }

        // Parse Label Detection — object classification
        List<string> labels = [];
        if (firstResponse.TryGetProperty("labelAnnotations", out JsonElement labelAnnotations)
            && labelAnnotations.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement annotation in labelAnnotations.EnumerateArray().Take(10))
            {
                string? desc = GetString(annotation, "description");
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    labels.Add(desc);
                }
            }
        }

        // Build search query — best guess label is strongest signal
        string? searchQuery = bestGuessLabel;
        if (string.IsNullOrWhiteSpace(searchQuery) && webEntities.Count > 0)
        {
            // Combine top 2 web entities as search query
            searchQuery = string.Join(" ", webEntities
                .OrderByDescending(e => e.Score)
                .Take(2)
                .Select(e => e.Description));
        }

        return new VisionIdentifyResult(
            BestGuessLabel: bestGuessLabel,
            WebEntities: webEntities,
            MatchingPages: matchingPages,
            SimilarImages: similarImages,
            Labels: labels,
            SearchQuery: searchQuery?.Trim());
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static VisionIdentifyResult EmptyResult() => new(
        BestGuessLabel: null,
        WebEntities: [],
        MatchingPages: [],
        SimilarImages: [],
        Labels: [],
        SearchQuery: null);
}
