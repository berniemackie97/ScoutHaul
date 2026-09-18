using System.Globalization;
using System.Text;

namespace HaulScout.Application.Common;

/// <summary>
/// The imported facts of a record, reduced to a string that is equal exactly when the facts are.
///
/// Used to tell a real refresh from an identical re-import. The obvious comparison — joining the
/// values — reports phantom changes: money read back from the database carries its column's scale
/// (40.00) while the same amount parsed from an export may not (40), and a time read back in UTC
/// prints differently from the same instant parsed with its local offset. So amounts are compared
/// to the cent and times by instant.
/// </summary>
public static class ImportFingerprint
{
    /// <summary>ASCII unit separator: cannot occur in a value, so two different fact lists never join to the same string.</summary>
    private const char Separator = (char)0x1F;

    /// <summary>
    /// JSON in a canonical form: object keys sorted, no whitespace, values as written.
    ///
    /// JSON columns are stored as jsonb, and Postgres rewrites jsonb — it reorders keys and changes
    /// spacing — so the text read back never equals the text written. Compared canonically, the same
    /// document is the same whatever the storage did to it. Text that is not JSON is returned as is.
    /// </summary>
    public static string? Json(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
            StringBuilder builder = new();
            WriteCanonical(document.RootElement, builder);
            return builder.ToString();
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }

    private static void WriteCanonical(System.Text.Json.JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                builder.Append('{');
                bool firstProperty = true;
                foreach (System.Text.Json.JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (!firstProperty) builder.Append(',');
                    firstProperty = false;
                    builder.Append(System.Text.Json.JsonSerializer.Serialize(property.Name)).Append(':');
                    WriteCanonical(property.Value, builder);
                }
                builder.Append('}');
                break;
            case System.Text.Json.JsonValueKind.Array:
                builder.Append('[');
                bool firstItem = true;
                foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
                {
                    if (!firstItem) builder.Append(',');
                    firstItem = false;
                    WriteCanonical(item, builder);
                }
                builder.Append(']');
                break;
            case System.Text.Json.JsonValueKind.Number:
                // Compared as amounts, so 1.6 and 1.60 are the same fee.
                builder.Append(element.TryGetDecimal(out decimal number)
                    ? number.ToString("0.############", CultureInfo.InvariantCulture)
                    : element.GetRawText());
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    public static string Of(params object?[] facts)
    {
        StringBuilder builder = new();
        foreach (object? fact in facts)
        {
            builder.Append(fact switch
            {
                null => "(none)",
                decimal amount => Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture),
                DateTimeOffset instant => instant.UtcTicks.ToString(CultureInfo.InvariantCulture),
                DateTime time => time.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => fact.ToString(),
            });
            builder.Append(Separator);
        }

        return builder.ToString();
    }
}
