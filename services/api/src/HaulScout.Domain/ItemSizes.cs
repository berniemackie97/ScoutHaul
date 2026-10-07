using System.Text.RegularExpressions;

namespace HaulScout.Domain;

/// <summary>
/// A size as a marketplace writes it ("L / US 12-14", "OS", "XXL"), read as a size system and a value from the size
/// lists — but only where the item's profile leaves no doubt which system it is. A bare "7" could be a men's or a
/// women's shoe, so it is not guessed.
/// </summary>
public static partial class ItemSizes
{
    public const string Letter = "US letter";
    public const string Numeric = "US numeric";
    public const string OneSize = "One size";

    private static readonly Dictionary<string, string> Letters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["XXS"] = "XXS",
        ["XS"] = "XS",
        ["S"] = "S",
        ["M"] = "M",
        ["L"] = "L",
        ["XL"] = "XL",
        ["XXL"] = "2XL",
        ["2XL"] = "2XL",
        ["XXXL"] = "3XL",
        ["3XL"] = "3XL",
        ["4XL"] = "4XL",
        ["5XL"] = "5XL",
        ["Small"] = "S",
        ["Medium"] = "M",
        ["Large"] = "L",
        ["X-Large"] = "XL",
        ["XX-Large"] = "2XL",
        ["Extra small"] = "XS",
        ["Extra large"] = "XL",
    };

    private static readonly HashSet<string> OneSizeSpellings = new(StringComparer.OrdinalIgnoreCase)
    {
        "One size", "OS", "O/S", "OSFA", "One size fits all", "One size fits most",
    };

    /// <summary>The system and value a written size names, or null when the profile's systems cannot say which.</summary>
    public static (string System, string Value)? Read(string? written, ItemProfile profile)
    {
        string whole = (written ?? string.Empty).Trim();
        // Vinted writes the letter and its US range together ("L / US 12-14"); the letter is the size.
        string size = MarketplaceRange().Replace(whole, string.Empty).Trim();
        if (size.Length == 0)
        {
            return null;
        }

        (string System, string Value)? read =
            OneSizeSpellings.Contains(whole) ? (OneSize, OneSize)
            : Letters.TryGetValue(size, out string? letter) ? (Letter, letter)
            : int.TryParse(size, out int number) && number is >= 0 and <= 60 && !profile.SizeSystems.Any(IsShoeSystem) ? (Numeric, size)
            : null;
        return read is { } found && profile.SizeSystems.Contains(found.System, StringComparer.OrdinalIgnoreCase) ? found : null;
    }

    private static bool IsShoeSystem(string system) => system.StartsWith("Shoe", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s*(?:/|\().*$")]
    private static partial Regex MarketplaceRange();
}
