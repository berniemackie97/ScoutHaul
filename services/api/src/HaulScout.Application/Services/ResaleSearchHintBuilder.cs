using System.Text.Json;
using System.Text.RegularExpressions;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public static class ResaleSearchHintBuilder
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] LowSignalFields =
    [
        "category",
        "condition",
        "notes",
        "description",
    ];

    public static ResaleSearchHints FromListingRequest(GenerateListingRequest request)
    {
        string query = BuildQuery(
            request.Title,
            request.Brand,
            request.Model,
            request.AttributesJson,
            request.Condition,
            request.Notes);

        return FromQuery(query);
    }

    public static ResaleSearchHints FromQuery(string query)
    {
        string normalized = NormalizeQuery(query);
        string encoded = Uri.EscapeDataString(normalized);
        string url = $"https://www.ebay.com/sch/i.html?_nkw={encoded}&LH_Sold=1&LH_Complete=1&_sop=13";
        return new ResaleSearchHints(normalized, url);
    }

    private static string BuildQuery(
        string title,
        string? brand,
        string? model,
        string? attributesJson,
        string? condition,
        string? notes)
    {
        var terms = new List<string>();
        Add(terms, brand);
        Add(terms, title);
        Add(terms, model);

        foreach (string attributeTerm in ExtractAttributeTerms(attributesJson))
        {
            Add(terms, attributeTerm);
        }

        // Condition can materially change comps for collectibles/cards, electronics, footwear, etc.
        Add(terms, condition);

        // Notes are useful when they contain exact facts such as PSA 9, Japanese, lot of 12,
        // Unbroken Bonds, model numbers, storage size, etc. They are normalized and capped below.
        Add(terms, notes);

        return NormalizeQuery(string.Join(' ', terms));
    }

    private static void Add(List<string> terms, string? value)
    {
        string normalized = NormalizeQuery(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        terms.Add(normalized);
    }

    private static IReadOnlyList<string> ExtractAttributeTerms(string? attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson))
        {
            return [];
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(attributesJson);
            return ExtractPrimitiveValues(doc.RootElement, parentName: null).ToArray();
        }
        catch (JsonException)
        {
            return [attributesJson];
        }
    }

    private static IEnumerable<string> ExtractPrimitiveValues(JsonElement element, string? parentName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    foreach (string value in ExtractPrimitiveValues(property.Value, property.Name))
                    {
                        yield return value;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string value in ExtractPrimitiveValues(item, parentName))
                    {
                        yield return value;
                    }
                }
                break;
            case JsonValueKind.String:
                if (!IsLowSignal(parentName))
                {
                    yield return element.GetString() ?? string.Empty;
                }
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (!IsLowSignal(parentName))
                {
                    yield return element.ToString();
                }
                break;
        }
    }

    private static bool IsLowSignal(string? fieldName) =>
        fieldName is not null && LowSignalFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string withoutLabels = Regex.Replace(value, @"\b[A-Z][A-Za-z ]{1,24}:\s*", " ", RegexOptions.CultureInvariant);
        string withoutJsonPunctuation = withoutLabels
            .Replace('{', ' ')
            .Replace('}', ' ')
            .Replace('[', ' ')
            .Replace(']', ' ')
            .Replace('"', ' ')
            .Replace(',', ' ');

        string normalized = WhitespaceRegex.Replace(withoutJsonPunctuation, " ").Trim();
        const int maxLength = 180;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].Trim();
    }
}
