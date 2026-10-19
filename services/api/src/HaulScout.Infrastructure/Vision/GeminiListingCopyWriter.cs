using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Appraisal;

namespace HaulScout.Infrastructure.Vision;

/// <summary>Gemini writes the title and description; the appraisal supplies the comps it may mention.</summary>
internal sealed class GeminiListingCopyWriter(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GeminiListingCopyWriter> logger) : IListingCopyWriter
{
    private string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gemini:ApiKey"]);

    public async Task<ListingCopy?> WriteAsync(string details, AppraisalDto? appraisal, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        ListingCopyDto? copy = await GenerateCopyAsync(details, appraisal, apiKey, cancellationToken);
        return string.IsNullOrWhiteSpace(copy?.Title)
            ? null
            : new ListingCopy(copy.Title.Trim(), (copy.Description ?? string.Empty).Trim());
    }

    private const string SystemPrompt =
        "You are an expert reseller who writes high-converting secondhand marketplace listings (eBay, " +
        "Poshmark, Mercari, Facebook Marketplace). From the item details, and the comps provided, " +
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
        AppraisalDto? appraisal,
        string apiKey,
        CancellationToken cancellationToken)
    {
        StringBuilder context = new();
        context.Append("ITEM DETAILS:\n").Append(details).Append("\n\n");
        if (appraisal?.Comps.Where(comp => comp.Included).Take(8).ToArray() is { Length: > 0 } comps)
        {
            context.Append("COMPS THE PRICE IS BASED ON:\n");
            foreach (CompDto comp in comps)
            {
                string condition = comp.Condition is { Length: > 0 } stated ? $" {stated}" : string.Empty;
                context.Append($"- ${comp.Price:0.##} [{comp.Kind}]{condition} — {comp.Title}\n");
            }
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

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "Marketplace title, max 80 chars, keyword-rich" },
            description = new { type = "string", description = "Honest scannable listing description" },
        },
        required = new[] { "title", "description" },
    };

    private sealed class ListingCopyDto
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
}
