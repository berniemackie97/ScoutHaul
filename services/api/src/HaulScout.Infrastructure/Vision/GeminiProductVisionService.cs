using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Vision;

/// <summary>
/// THE product identifier. Gemini multimodal (free tier: 1,500 req/day) reads the photo, reasons
/// about the item, and returns a specific product name, brand, model, category, condition, and a
/// conservative used-resale valuation as structured JSON — identification and valuation confidence
/// kept separate. No reverse-image search, no OCR: this is the single source of truth.
/// </summary>
internal sealed class GeminiProductVisionService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiProductVisionService> logger) : IVisionIdentificationService
{
    // Free-tier model. Configurable so it can be bumped without a code change.
    private string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]);

    public async Task<VisionIdentifyResult> IdentifyAsync(string? imageBase64, string? context, CancellationToken cancellationToken)
    {
        ProductIdentification? product = await IdentifyProductAsync(imageBase64, context, cancellationToken);
        return new VisionIdentifyResult(
            Product: product,
            SearchQuery: BuildSearchQuery(product),
            DetectedText: product?.IdentifyingText);
    }

    private static string? BuildSearchQuery(ProductIdentification? p)
    {
        if (p is null) return null;
        if (!string.IsNullOrWhiteSpace(p.Brand) && !string.IsNullOrWhiteSpace(p.Model))
            return $"{p.Brand} {p.Model}".Trim();
        if (!string.IsNullOrWhiteSpace(p.Name)) return p.Name;
        return p.SearchKeywords.Count > 0 ? p.SearchKeywords[0] : null;
    }

    // The prompt is composed from these sections so it adapts to the inputs: a photo, typed facts, or
    // both. The valuation/range/comps rules are shared; only the IDENTIFY guidance changes by mode.
    private const string IntroShared =
        "You are an expert reseller appraiser with strong product knowledge. Do TWO separate jobs and keep " +
        "their confidences separate: (1) IDENTIFY the main item, (2) ESTIMATE its used resale value.\n\n";

    private const string BothInputsNote =
        "INPUTS: a PHOTO of the item AND known facts the user typed. Trust the user's facts as ground truth " +
        "for identity (brand/model/size/edition); use the photo to confirm them, pin the exact variant, and " +
        "judge condition. If the photo clearly contradicts a stated fact, keep the user's fact for identity " +
        "but record the conflict in missing_information.\n\n";

    private const string PhotoIdentify =
        "FOCUS: if several objects are visible, identify the SINGLE most prominent item — the one centered " +
        "in the frame and closest to the camera. That is what the user is pointing at; ignore background " +
        "clutter, surfaces, and surrounding objects.\n\n" +
        "IDENTIFY — identify the item primarily from its VISUAL APPEARANCE: shape, form factor, materials, " +
        "color, proportions, distinctive design features, parts, and how it is built. Recognize the object " +
        "the way a knowledgeable human would at a glance, EVEN WHEN THERE IS NO VISIBLE TEXT, LOGO, OR " +
        "BRANDING. If text/logos/model numbers ARE visible, use them to get more specific (exact brand, " +
        "model, variant). But never depend on text: a black furniture caster, a ceramic vase, a cast-iron " +
        "skillet, a vintage camera, a power tool, or a hand tool are all identifiable by sight alone — name " +
        "what the object IS and its most likely product type/category. NEVER return 'unidentified' or a " +
        "blank name for an object that is clearly visible in the frame; at minimum give its specific object " +
        "type (e.g. 'cast-iron skillet', 'mid-century wooden rocking chair', 'cordless drill driver'). Only " +
        "stay vague when the image itself is too blurry, dark, or cropped to tell. NEVER invent brand names, " +
        "model numbers, years, editions, sizes, certifications, or authenticity you cannot actually see or " +
        "confidently infer from appearance; put unknowns in missing_information. Set variant_confidence = " +
        "certainty of the EXACT variant (high only when the specific brand/edition/model is clear from text " +
        "or unmistakable design; medium when you know the object type and likely product; low when only the " +
        "broad category is clear).\n\n";

    private const string TextIdentify =
        "INPUT: there is NO photo — only the known facts the user typed below. IDENTIFY the item from those " +
        "facts plus your product knowledge. Use any brand, model number, size, set/edition, or year in the " +
        "facts to pin the EXACT variant. Do NOT invent attributes the facts don't support and do NOT assume " +
        "visual details you were not given; put unknowns in missing_information. If the facts are too sparse " +
        "to pin the exact variant, name the specific object type / category and stay at that level. Set " +
        "variant_confidence = how specifically the facts pin the item (high only when brand + model/variant " +
        "are explicit; medium when the product type and likely model are clear; low when only the broad " +
        "category is given).\n\n";

    private const string ValueShared =
        "VALUE (used/secondhand resale ONLY — never retail, MSRP, replacement cost, or optimistic asking " +
        "price): Reason from typical SOLD prices on resale markets (eBay sold, Facebook Marketplace, Mercari, " +
        "Poshmark, StockX). You do NOT have live market data — never claim you checked listings. Assume typical " +
        "USED condition unless told otherwise. low = quick-sale or lower-condition value; high = realistic " +
        "private-party value in good used condition (NOT a rare-outlier price). Be CONSERVATIVE: do not assume " +
        "the rare/high-value variant unless you can clearly tell. Price damaged, incomplete, untested, " +
        "counterfeit-risk, or missing-accessory items LOWER.\n\n" +
        "RANGE WIDTH: Generic or variant-heavy items (e.g. iPhone, Nike Air Jordan 1, Pokemon card, Levi's " +
        "jeans, Rolex watch) get a WIDE range unless the exact variant is clear. Use tight only when BOTH the " +
        "product and its condition are clear; otherwise moderate or wide. price_range_type MUST match the width " +
        "you chose. valuation_confidence = certainty of the price given the identification.\n\n" +
        "If you cannot identify the item well enough, return a broad CATEGORY-level estimate and list the " +
        "missing details. key_value_factors must name what drives value (variant, condition, year, model, size, " +
        "edition, authenticity, completeness, accessories). recommended_next_steps tells the reseller how to " +
        "narrow it down. Provide 3-6 search keyword phrases for finding comps.\n" +
        "comps: give 3-6 REPRESENTATIVE comparable price points from your market knowledge — mix recent " +
        "SOLD prices (status \"sold\") with current ACTIVE asking prices (status \"active\"), each with the " +
        "condition it reflects and a short note. These are ESTIMATES from training knowledge, NOT live " +
        "listings — never invent URLs, exact dates, or claim you checked current listings. " +
        "Respond ONLY with valid JSON matching the schema.";

    // Composes the system prompt for the given inputs. Photo present → visual identification (with a
    // ground-truth note when facts are also given); facts only → text identification.
    private static string BuildSystemPrompt(bool hasImage, bool hasFacts) =>
        IntroShared
        + (hasImage
            ? (hasFacts ? BothInputsNote : "") + PhotoIdentify
            : TextIdentify)
        + ValueShared;

    private async Task<ProductIdentification?> IdentifyProductAsync(string? imageBase64, string? context, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Gemini API key is not configured");
            return null;
        }

        bool hasImage = !string.IsNullOrWhiteSpace(imageBase64);
        string? knownFacts = context?.Trim();
        bool hasFacts = !string.IsNullOrWhiteSpace(knownFacts);
        if (!hasImage && !hasFacts)
        {
            // Nothing to identify from. The controller guards this; fail closed here too.
            return null;
        }

        // Dynamic prompt → (optional) user-provided known facts → (optional) image. Facts are ground
        // truth the model trusts to nail the exact variant and refine the valuation.
        List<object> parts = [new { text = BuildSystemPrompt(hasImage, hasFacts) }];
        if (hasFacts)
        {
            parts.Add(new { text = "Known facts provided by the user (treat as ground truth; use to disambiguate the variant and refine valuation):\n" + knownFacts });
        }
        if (hasImage)
        {
            parts.Add(new { inline_data = new { mime_type = "image/jpeg", data = imageBase64 } });
        }

        var requestBody = new
        {
            contents = new[] { new { parts = parts.ToArray() } },
            generationConfig = new
            {
                temperature = 0.1, // deterministic — we want facts, not creativity
                responseMimeType = "application/json",
                responseSchema = ResponseSchema,
            },
        };

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={apiKey}";
        string json = JsonSerializer.Serialize(requestBody);

        // gemini-2.5-flash returns transient 503 UNAVAILABLE ("high demand") on ~1 in 6 calls, plus
        // 429 under free-tier burst. Retry up to 4× with exponential backoff so a momentary spike
        // doesn't surface to the user as "Not identified" — measured failure after 4 tries is <0.1%.
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using StringContent content = new(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(url, content, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gemini vision request threw (attempt {Attempt}/{Max})", attempt, maxAttempts);
                if (attempt < maxAttempts) { await Backoff(attempt, cancellationToken); continue; }
                throw new VisionUnavailableException("upstream_unavailable", "Could not reach the AI vision service.");
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    string payload = await response.Content.ReadAsStringAsync(cancellationToken);
                    return ParseResponse(payload);
                }

                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                (string code, string detail) = ClassifyError(response.StatusCode, body);
                logger.LogWarning("Gemini returned {StatusCode} ({Code}, attempt {Attempt}/{Max}): {Body}",
                    response.StatusCode, code, attempt, maxAttempts, body[..Math.Min(body.Length, 300)]);

                bool transient = response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.ServiceUnavailable
                    or System.Net.HttpStatusCode.InternalServerError;
                // A daily-quota exhaustion won't clear in seconds — fail fast rather than burn retries.
                if (transient && code != "quota_exhausted" && attempt < maxAttempts)
                {
                    await Backoff(attempt, cancellationToken);
                    continue;
                }
                throw new VisionUnavailableException(code, detail);
            }
        }

        throw new VisionUnavailableException("upstream_error", "The AI vision service is unavailable.");
    }

    private static async Task Backoff(int attempt, CancellationToken ct) =>
        await Task.Delay(TimeSpan.FromMilliseconds(400 * (1 << (attempt - 1))), ct);

    // Map a failed Gemini HTTP response to a stable client code + user-facing detail.
    private static (string code, string detail) ClassifyError(System.Net.HttpStatusCode status, string body)
    {
        if (status == System.Net.HttpStatusCode.TooManyRequests)
        {
            return body.Contains("PerDay", StringComparison.OrdinalIgnoreCase)
                ? ("quota_exhausted", "The daily AI request limit has been reached.")
                : ("rate_limited", "Too many AI requests right now. Try again in a moment.");
        }
        if (status == System.Net.HttpStatusCode.ServiceUnavailable)
            return ("upstream_unavailable", "The AI service is busy. Try again shortly.");
        return ("upstream_error", "The AI service returned an error.");
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

            // variant_confidence is the identification confidence (kept as Confidence for compat);
            // fall back to legacy single "confidence" if present.
            string variantConfidence = NormalizeConfidence(dto.VariantConfidence ?? dto.Confidence);
            string valuationConfidence = NormalizeConfidence(dto.ValuationConfidence ?? dto.VariantConfidence ?? dto.Confidence);
            string priceRangeType = (dto.PriceRangeType ?? "").ToLowerInvariant() switch
            {
                "tight" => "tight",
                "moderate" => "moderate",
                "wide" => "wide",
                _ => "moderate",
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
                SearchKeywords: CleanList(dto.SearchKeywords, 6),
                Confidence: variantConfidence,
                ValuationConfidence: valuationConfidence,
                PriceRangeType: priceRangeType,
                KeyValueFactors: CleanList(dto.KeyValueFactors, 6),
                MissingInformation: CleanList(dto.MissingInformation, 6),
                RecommendedNextSteps: CleanList(dto.RecommendedNextSteps, 5),
                Rationale: Clean(dto.Rationale),
                Comps: GeminiResaleEstimator.CleanComps(dto.Comps),
                ConditionQuality: Clean(dto.ConditionQuality)?.ToLowerInvariant());
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

    private static string NormalizeConfidence(string? value) => (value ?? "low").ToLowerInvariant() switch
    {
        "high" => "high",
        "medium" => "medium",
        _ => "low",
    };

    private static IReadOnlyList<string> CleanList(List<string>? items, int max) =>
        (items ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Take(max)
            .ToArray();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Gemini structured-output schema (OpenAPI subset). Enforces the field set so the model
    // cannot drift. Identification and valuation confidence are deliberately separate.
    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "Specific identified item name, or category if unsure" },
            brand = new { type = "string" },
            model = new { type = "string", description = "Model or specific variant, only if visible" },
            category = new { type = "string" },
            color = new { type = "string" },
            condition = new { type = "string", description = "Condition assumption from the photo" },
            conditionQuality = new { type = "string", @enum = new[] { "new", "like-new", "good", "fair", "poor", "for-parts" }, description = "Rough overall condition grade visible in the photo" },
            estimatedResaleLow = new { type = "number", description = "Quick-sale / lower-condition USED resale USD" },
            estimatedResaleHigh = new { type = "number", description = "Good-used private-party resale USD (not an outlier)" },
            identifyingText = new { type = "string", description = "Text/model numbers actually visible on the item" },
            searchKeywords = new { type = "array", items = new { type = "string" } },
            variantConfidence = new { type = "string", @enum = new[] { "high", "medium", "low" }, description = "Certainty of the exact variant" },
            valuationConfidence = new { type = "string", @enum = new[] { "high", "medium", "low" }, description = "Certainty of the price" },
            priceRangeType = new { type = "string", @enum = new[] { "tight", "moderate", "wide" } },
            keyValueFactors = new { type = "array", items = new { type = "string" }, description = "What drives this item's value" },
            missingInformation = new { type = "array", items = new { type = "string" }, description = "Details needed to price it precisely" },
            recommendedNextSteps = new { type = "array", items = new { type = "string" } },
            rationale = new { type = "string", description = "One-to-two sentence valuation rationale" },
            comps = new
            {
                type = "array",
                description = "3-6 representative SOLD and ACTIVE comp price points from market knowledge (not live)",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        price = new { type = "number" },
                        status = new { type = "string", @enum = new[] { "sold", "active" } },
                        condition = new { type = "string" },
                        note = new { type = "string" },
                    },
                    required = new[] { "price", "status" },
                },
            },
        },
        required = new[] { "name", "category", "variantConfidence", "valuationConfidence", "priceRangeType", "searchKeywords" },
    };

    private sealed class GeminiProductDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("color")] public string? Color { get; set; }
        [JsonPropertyName("condition")] public string? Condition { get; set; }
        [JsonPropertyName("conditionQuality")] public string? ConditionQuality { get; set; }
        [JsonPropertyName("estimatedResaleLow")] public decimal EstimatedResaleLow { get; set; }
        [JsonPropertyName("estimatedResaleHigh")] public decimal EstimatedResaleHigh { get; set; }
        [JsonPropertyName("identifyingText")] public string? IdentifyingText { get; set; }
        [JsonPropertyName("searchKeywords")] public List<string>? SearchKeywords { get; set; }
        // Back-compat: older responses used a single "confidence"; new schema splits the two.
        [JsonPropertyName("confidence")] public string? Confidence { get; set; }
        [JsonPropertyName("variantConfidence")] public string? VariantConfidence { get; set; }
        [JsonPropertyName("valuationConfidence")] public string? ValuationConfidence { get; set; }
        [JsonPropertyName("priceRangeType")] public string? PriceRangeType { get; set; }
        [JsonPropertyName("keyValueFactors")] public List<string>? KeyValueFactors { get; set; }
        [JsonPropertyName("missingInformation")] public List<string>? MissingInformation { get; set; }
        [JsonPropertyName("recommendedNextSteps")] public List<string>? RecommendedNextSteps { get; set; }
        [JsonPropertyName("rationale")] public string? Rationale { get; set; }
        [JsonPropertyName("comps")] public List<GeminiResaleEstimator.CompDto>? Comps { get; set; }
    }
}
