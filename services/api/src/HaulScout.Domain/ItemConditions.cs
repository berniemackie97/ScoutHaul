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

    /// <summary>What each condition means, shown beside it so the same word reads the same way on every item.</summary>
    public static readonly IReadOnlyDictionary<string, string> Hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Factory sealed"] = "Unopened, original seal",
        ["New with tags"] = "Unworn, tags attached",
        ["New without tags"] = "Unworn, no tags",
        ["New"] = "Unused",
        ["Open box"] = "Unused, box opened",
        ["Refurbished"] = "Restored to working order",
        ["Like new"] = "Barely used, no flaws",
        ["Excellent"] = "Light use, no flaws",
        ["Very good"] = "Minor wear",
        ["Good"] = "Visible wear, noted",
        ["Fair"] = "Flaws, priced to match",
        ["Poor"] = "Heavy wear or damage",
        ["For parts"] = "Not working",
    };

    private static readonly string[] Worn = ["New with tags", "New without tags", "Like new", "Excellent", "Very good", "Good", "Fair", "Poor"];
    private static readonly string[] Boxed = ["New with tags", "New without tags", "New", "Like new", "Excellent", "Very good", "Good", "Fair", "Poor"];
    private static readonly string[] Device = ["Factory sealed", "New", "Open box", "Refurbished", "Like new", "Excellent", "Very good", "Good", "Fair", "For parts"];
    private static readonly string[] Sealed = ["Factory sealed", "New", "Like new", "Excellent", "Very good", "Good", "Fair", "Poor"];

    /// <summary>
    /// The conditions that describe each kind of item, best first: a shirt is new with tags or worn, a phone sealed,
    /// refurbished or for parts. A kind not listed, or an item filed nowhere yet, is offered every condition.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Scales = new Dictionary<string, IReadOnlyList<string>>
    {
        ["apparel"] = Worn,
        ["baby"] = ["New with tags", "New without tags", "New", "Like new", "Excellent", "Good", "Fair", "Poor"],
        ["footwear"] = Boxed,
        ["accessories"] = Boxed,
        ["watches"] = ["New", "Like new", "Excellent", "Very good", "Good", "Fair", "Poor", "For parts"],
        ["electronics"] = Device,
        ["video-games"] = ["Factory sealed", "New", "Like new", "Excellent", "Very good", "Good", "Fair", "Poor", "For parts"],
        ["collectibles"] = Sealed,
        ["media"] = Sealed,
        ["equipment"] = ["New", "Open box", "Refurbished", "Like new", "Excellent", "Good", "Fair", "Poor", "For parts"],
        ["sporting"] = ["New with tags", "New", "Open box", "Like new", "Excellent", "Very good", "Good", "Fair", "Poor"],
        ["home"] = ["Factory sealed", "New", "Open box", "Like new", "Excellent", "Very good", "Good", "Fair", "Poor"],
    };

    /// <summary>The scale for a kind of item; empty means every condition applies.</summary>
    public static IReadOnlyList<string> ScaleFor(string profileKey) => Scales.TryGetValue(profileKey, out IReadOnlyList<string>? scale) ? scale : [];

    /// <summary>The conditions an organization uses — the list above, with its own added or removed.</summary>
    public static Vocabulary From(IEnumerable<string> organizationConditions) => new(organizationConditions, OtherSpellings);
}
