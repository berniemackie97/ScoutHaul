namespace HaulScout.Domain;

/// <summary>The conditions an item can be in, best first, and how marketplaces write them.</summary>
public static class ItemConditions
{
    public static readonly IReadOnlyList<string> Terms =
    [
        "Factory sealed", "New with tags", "New without tags", "New", "Open box", "Refurbished", "Like new", "Excellent",
        "Very good", "Good", "Fair", "Poor", "For parts",
    ];

    /// <summary>
    /// Poshmark's codes, eBay's "Pre-owned" grades, Vinted's and Depop's wording. A plain "Used" says only that it is
    /// not new, so it reads as Good.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> OtherSpellings = new Dictionary<string, string>
    {
        ["NWT"] = "New with tags",
        ["NWOT"] = "New without tags",
        ["Sealed"] = "Factory sealed",
        ["Brand new"] = "New",
        ["New in box"] = "New",
        ["NIB"] = "New",
        ["ULN"] = "Like new",
        ["Mint"] = "Like new",
        ["Near mint"] = "Like new",
        ["NM"] = "Like new",
        ["Used - NM"] = "Like new",
        ["Pre-owned - Excellent"] = "Excellent",
        ["Lightly used"] = "Excellent",
        ["Light use"] = "Excellent",
        ["LU"] = "Excellent",
        ["Gently used"] = "Excellent",
        ["Used - LU"] = "Excellent",
        ["Pre-owned - Good"] = "Good",
        ["UG"] = "Good",
        ["Used"] = "Good",
        ["Pre-owned"] = "Good",
        ["Moderate use"] = "Good",
        ["Moderate wear"] = "Good",
        ["Pre-owned - Fair"] = "Fair",
        ["UF"] = "Fair",
        ["Satisfactory"] = "Fair",
        ["Needs minor repair"] = "Fair",
        ["Needs alteration"] = "Fair",
        ["Heavy use"] = "Poor",
        ["Well worn"] = "Poor",
        ["Damaged"] = "Poor",
        ["Needs major repair"] = "Poor",
        ["For parts or not working"] = "For parts",
        ["Not working"] = "For parts",
    };

    public static readonly Vocabulary Standard = From(Terms);

    /// <summary>The conditions an organization uses — the list above, with its own added or removed.</summary>
    public static Vocabulary From(IEnumerable<string> organizationConditions) => new(organizationConditions, OtherSpellings);
}
