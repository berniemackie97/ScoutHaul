namespace HaulScout.Domain;

/// <summary>The colors an item can be described in, and how many it may have.</summary>
public static class ItemColors
{
    /// <summary>Poshmark, Depop and Vinted each take two; beyond that an item is Multicolor or Patterned.</summary>
    public const int Max = 2;

    public static readonly IReadOnlyList<string> Terms =
    [
        "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink", "Brown", "Tan",
        "Khaki", "Beige", "Cream", "Navy", "Teal", "Maroon", "Gold", "Rose gold", "Bronze", "Natural", "Multicolor",
        "Patterned", "Clear",
    ];

    /// <summary>How marketplaces and people also write them. Shades fold into the color a buyer would filter by.</summary>
    public static readonly IReadOnlyDictionary<string, string> OtherSpellings = new Dictionary<string, string>
    {
        ["Grey"] = "Gray",
        ["Charcoal"] = "Gray",
        ["Multi"] = "Multicolor",
        ["Multicolour"] = "Multicolor",
        ["Multi color"] = "Multicolor",
        ["Burgundy"] = "Maroon",
        ["Wine"] = "Maroon",
        ["Off white"] = "Cream",
        ["Ivory"] = "Cream",
        ["Camel"] = "Tan",
        ["Taupe"] = "Beige",
        ["Nude"] = "Beige",
        ["Olive"] = "Green",
        ["Turquoise"] = "Teal",
        ["Indigo"] = "Blue",
        ["Violet"] = "Purple",
        ["Lavender"] = "Purple",
        ["Fuchsia"] = "Pink",
        ["Magenta"] = "Pink",
        ["Mustard"] = "Yellow",
        ["Gold tone"] = "Gold",
        ["Silver tone"] = "Silver",
        ["Transparent"] = "Clear",
        ["Print"] = "Patterned",
        ["Floral"] = "Patterned",
        ["Plaid"] = "Patterned",
        ["Striped"] = "Patterned",
        ["Camo"] = "Patterned",
        ["Animal print"] = "Patterned",
    };

    /// <summary>The colors an organization uses — the list above, with its own added or removed — read as a vocabulary.</summary>
    public static Vocabulary From(IEnumerable<string> organizationColors) => new(organizationColors, OtherSpellings);

    public static readonly Vocabulary Standard = From(Terms);
}
