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
                        new { type = "TEXT_DETECTION", maxResults = 1 },
                        new { type = "LOGO_DETECTION", maxResults = 5 },
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

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Google Cloud Vision returned {StatusCode}: {Body}",
                    response.StatusCode, errorBody[..Math.Min(errorBody.Length, 500)]);
                return EmptyResult();
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
                return EmptyResult();
            }
            using (doc)
            {
                return ParseResponse(doc);
            }
        }
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

        // Parse Logo Detection — brand logos
        List<string> logos = [];
        if (firstResponse.TryGetProperty("logoAnnotations", out JsonElement logoAnnotations)
            && logoAnnotations.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement annotation in logoAnnotations.EnumerateArray().Take(5))
            {
                string? desc = GetString(annotation, "description");
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    logos.Add(desc);
                }
            }
        }

        // Parse Text Detection — OCR from image
        string? detectedText = null;
        if (firstResponse.TryGetProperty("textAnnotations", out JsonElement textAnnotations)
            && textAnnotations.ValueKind == JsonValueKind.Array
            && textAnnotations.GetArrayLength() > 0)
        {
            // First annotation is full text block
            detectedText = GetString(textAnnotations[0], "description")?.Trim();
        }

        // Build search query — cascading: bestGuessLabel > entities > labels
        string? searchQuery = bestGuessLabel;
        if (string.IsNullOrWhiteSpace(searchQuery) && webEntities.Count > 0)
        {
            searchQuery = string.Join(" ", webEntities
                .OrderByDescending(e => e.Score)
                .Take(2)
                .Select(e => e.Description));
        }
        if (string.IsNullOrWhiteSpace(searchQuery) && labels.Count > 0)
        {
            // Fallback to labels — filter out overly generic ones
            HashSet<string> genericLabels = ["Product", "Material", "Rectangle", "Font",
                "Design", "Pattern", "Art", "Electric blue", "Graphics", "Brand",
                "Logo", "Illustration", "Circle", "Symbol", "Line", "Parallel",
                "Symmetry", "Technology", "Event", "Room", "Gadget"];

            string[] useful = labels
                .Where(l => !genericLabels.Contains(l) && l.Length > 2)
                .Take(3)
                .ToArray();

            if (useful.Length > 0)
            {
                searchQuery = string.Join(" ", useful);
            }
        }

        return new VisionIdentifyResult(
            BestGuessLabel: bestGuessLabel,
            WebEntities: webEntities,
            MatchingPages: matchingPages,
            SimilarImages: similarImages,
            Labels: labels,
            DetectedLogos: logos,
            DetectedText: detectedText,
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
        DetectedLogos: [],
        DetectedText: null,
        SearchQuery: null);
}
