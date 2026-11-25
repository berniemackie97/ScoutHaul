using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HaulScout.Domain;

/// <summary>One flat-lay measurement of an item, such as pit to pit on a shirt.</summary>
public sealed record ItemMeasurement(string Key, string Label);

/// <summary>
/// The measurements each kind of item is sold by, stored as one JSON object of numbers plus its unit
/// (<c>{"unit":"in","pitToPit":19.5}</c>). Package size and weight are separate: they are for shipping, not buyers.
/// </summary>
public static class ItemMeasurements
{
    public const string UnitKey = "unit";
    public const string Inches = "in";
    public const string Centimeters = "cm";
    private const decimal Largest = 1000m;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ItemMeasurement>> ByProfile = new Dictionary<string, IReadOnlyList<ItemMeasurement>>
    {
        ["apparel"] =
        [
            new("pitToPit", "Pit to pit"), new("length", "Length"), new("shoulder", "Shoulder"), new("sleeve", "Sleeve"),
            new("waist", "Waist"), new("hip", "Hip"), new("rise", "Rise"), new("inseam", "Inseam"),
        ],
        ["baby"] = [new("pitToPit", "Pit to pit"), new("length", "Length"), new("inseam", "Inseam")],
        ["footwear"] = [new("insole", "Insole length"), new("width", "Sole width"), new("heel", "Heel height"), new("shaft", "Shaft height")],
    };

    public static IReadOnlyList<ItemMeasurement> For(string profileKey) =>
        ByProfile.TryGetValue(profileKey, out IReadOnlyList<ItemMeasurement>? measurements) ? measurements : [];

    /// <summary>
    /// The measurements the profile defines, each a positive number under 1000, with the unit in or cm (in when missing).
    /// Anything else is dropped; no measurements at all reads as none.
    /// </summary>
    public static string? Keep(string? measurementsJson, ItemProfile profile)
    {
        IReadOnlyList<ItemMeasurement> measurements = profile.Measurements;
        if (string.IsNullOrWhiteSpace(measurementsJson) || measurements.Count == 0)
        {
            return null;
        }

        JsonObject? source;
        try
        {
            source = JsonNode.Parse(measurementsJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (source is null)
        {
            return null;
        }

        JsonObject kept = [];
        foreach (ItemMeasurement measurement in measurements)
        {
            if (Number(source[measurement.Key]) is { } value)
            {
                kept[measurement.Key] = value;
            }
        }

        if (kept.Count == 0)
        {
            return null;
        }

        string unit = source[UnitKey] is JsonValue unitValue && unitValue.TryGetValue(out string? text) && string.Equals(text?.Trim(), Centimeters, StringComparison.OrdinalIgnoreCase)
            ? Centimeters
            : Inches;
        JsonObject result = new() { [UnitKey] = unit };
        foreach ((string key, JsonNode? value) in kept)
        {
            result[key] = value?.DeepClone();
        }

        return result.ToJsonString();
    }

    private static decimal? Number(JsonNode? node)
    {
        decimal? value = node switch
        {
            JsonValue number when number.TryGetValue(out decimal parsed) => parsed,
            JsonValue text when text.TryGetValue(out string? raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => null,
        };
        return value is > 0 and < Largest ? Math.Round(value.Value, 2) : null;
    }
}
