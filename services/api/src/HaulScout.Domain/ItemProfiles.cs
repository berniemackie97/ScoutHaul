using System.Text.Json;
using System.Text.Json.Nodes;
using HaulScout.Domain.Entities;

namespace HaulScout.Domain;

/// <summary>The optional item fields a profile can switch on. Title, category, condition, brand, money and shipping always apply.</summary>
public enum ItemField
{
    Model,
    Color,
    Size,
    FunctionalStatus,
    CompletenessStatus,
    AuthenticityStatus,
    PackagingStatus,
    ManufacturerPartNumber,
    SerialNumber,
    ManufactureYear,
    CountryOfOrigin,
}

/// <summary>How much a field matters before an item is listed.</summary>
public enum ListingReadiness
{
    Optional,
    /// <summary>Flagged when missing, but listing goes ahead.</summary>
    Recommended,
    /// <summary>The item is not ready to list without it.</summary>
    Required,
}

public sealed record ItemFieldSpec(ItemField Field, string Label, ListingReadiness Readiness = ListingReadiness.Optional);

/// <summary>A detail stored in the item's attributes. With options it is a pick list; without, free text.</summary>
public sealed record ItemAttribute(string Key, string Label, IReadOnlyList<string>? Options = null);

/// <summary>What describing one kind of item takes: which fields apply, what they are called, and its own details.</summary>
public sealed record ItemProfile(
    string Key,
    string Label,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ItemFieldSpec> Fields,
    IReadOnlyList<ItemAttribute> Attributes,
    IReadOnlyList<string> SizeSystems,
    string BrandLabel = "Brand")
{
    public bool Has(ItemField field) => Fields.Any(spec => spec.Field == field);
}

/// <summary>
/// Which fields an item has follows from what it is: a shirt has a size and no serial number, a console the reverse.
/// One table, served to every editor and enforced on save, so no surface invents its own idea of an item — and the
/// listing-readiness rules come from the same place instead of a second list of categories.
/// </summary>
public static class ItemProfiles
{
    private static ItemFieldSpec Model(string label = "Model") => new(ItemField.Model, label);
    private static readonly ItemFieldSpec Color = new(ItemField.Color, "Color");
    private static readonly ItemFieldSpec Size = new(ItemField.Size, "Size");
    private static readonly ItemFieldSpec Year = new(ItemField.ManufactureYear, "Year made");
    private static readonly ItemFieldSpec PartNumber = new(ItemField.ManufacturerPartNumber, "Manufacturer part number");
    private static readonly ItemFieldSpec Serial = new(ItemField.SerialNumber, "Serial number");
    private static readonly ItemFieldSpec Origin = new(ItemField.CountryOfOrigin, "Country of origin");

    private static ItemFieldSpec Functional(ListingReadiness readiness = ListingReadiness.Required) =>
        new(ItemField.FunctionalStatus, "Working condition", readiness);

    private static ItemFieldSpec Completeness(ListingReadiness readiness = ListingReadiness.Required) =>
        new(ItemField.CompletenessStatus, "Completeness", readiness);

    private static ItemFieldSpec Packaging(ListingReadiness readiness = ListingReadiness.Recommended) =>
        new(ItemField.PackagingStatus, "Packaging", readiness);

    private static ItemFieldSpec Authenticity(ListingReadiness readiness = ListingReadiness.Recommended) =>
        new(ItemField.AuthenticityStatus, "Authenticity", readiness);

    private static readonly ItemAttribute Department = new("department", "Department", ["Women", "Men", "Unisex", "Girls", "Boys", "Baby"]);
    private static readonly ItemAttribute Material = new("material", "Material");

    private static readonly string[] ClothingSizes =
    [
        "US letter", "US numeric", "US women's numeric", "US juniors numeric", "US women's plus", "US men's numeric",
        "Waist/Inseam", "EU numeric", "UK numeric", "AU numeric", "IT numeric", "FR numeric", "Youth", "One size",
    ];

    private static readonly string[] ShoeSizes = ["Shoe US M", "Shoe US W", "Shoe US Youth", "Shoe EU", "Shoe UK"];

    /// <summary>Before a category is chosen: only the fields every item has.</summary>
    public static readonly ItemProfile General = new("general", "General", [], [], [], []);

    public static readonly IReadOnlyList<ItemProfile> All =
    [
        new("apparel", "Clothing", ["Clothing"], [Color, Size, Authenticity()], [Department, Material], ClothingSizes),
        new("baby", "Baby", ["Baby"], [Model(), Color, Size, Completeness(), Packaging(ListingReadiness.Optional)], [], ["Youth", "One size"]),
        new("footwear", "Shoes", ["Shoes"], [Model(), Color, Size, Authenticity(), Packaging()], [Department, Material], ShoeSizes),
        new("accessories", "Bags & jewelry", ["Handbags", "Jewelry"], [Model(), Color, Authenticity(), Packaging()], [Material], []),
        new(
            "watches",
            "Watches",
            ["Watches"],
            [Model(), Color, Serial, Year, Functional(ListingReadiness.Recommended), Completeness(ListingReadiness.Optional), Authenticity(), Packaging()],
            [Material],
            []),
        new(
            "electronics",
            "Electronics",
            ["Tech", "Phones", "Cameras", "Audio", "TV", "Electronics", "Appliances"],
            [Model(), Color, PartNumber, Serial, Year, Functional(), Completeness(), Packaging()],
            [new ItemAttribute("storage", "Storage capacity")],
            []),
        new(
            "video-games",
            "Video games",
            ["Video Games"],
            [Model("Platform"), Color, Year, Functional(), Completeness(), Packaging()],
            [new ItemAttribute("region", "Region", ["NTSC-U (North America)", "PAL (Europe)", "NTSC-J (Japan)", "Region free"])],
            [],
            BrandLabel: "Console maker or publisher"),
        new(
            "collectibles",
            "Collectibles & toys",
            ["Collectibles", "Toys"],
            [Model("Set or series"), Year, Completeness(), Authenticity(), Packaging(), Origin],
            [new ItemAttribute("grader", "Grading company", ["PSA", "BGS", "CGC", "SGC", "Ungraded"]), new ItemAttribute("grade", "Grade")],
            []),
        new(
            "media",
            "Books & media",
            ["Books", "Media"],
            [Year, Completeness(), Packaging()],
            [
                new ItemAttribute("creator", "Author or artist"),
                new ItemAttribute("format", "Format", ["Hardcover", "Paperback", "Vinyl", "CD", "Cassette", "DVD", "Blu-ray", "VHS", "Digital code"]),
            ],
            [],
            BrandLabel: "Publisher or label"),
        new(
            "equipment",
            "Tools & equipment",
            ["Tools", "Garden", "Fitness", "Musical Instruments", "Automotive", "Industrial", "Office"],
            [Model(), Color, PartNumber, Serial, Functional(), Completeness()],
            [],
            []),
        new("sporting", "Sporting goods", ["Sporting Goods"], [Model(), Color, Authenticity(), Completeness(ListingReadiness.Optional)], [Material], []),
        new(
            "home",
            "Home & lifestyle",
            ["Furniture", "Decor", "Home", "Crafts", "Health & Beauty", "Pet Supplies", "Other"],
            [Model(), Color, Completeness(ListingReadiness.Optional), Packaging(ListingReadiness.Optional), Origin],
            [Material],
            []),
    ];

    private static readonly Dictionary<string, ItemProfile> ByCategory = All
        .SelectMany(profile => profile.Categories.Select(category => (category, profile)))
        .ToDictionary(entry => entry.category, entry => entry.profile, StringComparer.OrdinalIgnoreCase);

    /// <summary>The profile for a category; the general one when there is none yet.</summary>
    public static ItemProfile For(string? category) =>
        category is not null && ByCategory.TryGetValue(category, out ItemProfile? profile) ? profile : General;

    /// <summary>
    /// Empties every field the item's kind does not have, so a field hidden by the editor cannot keep a value no one can
    /// see, and an import cannot give a video game a shoe size.
    /// </summary>
    public static void ClearInapplicable(InventoryItem item)
    {
        ItemProfile profile = For(item.Category);
        if (!profile.Has(ItemField.Model)) item.Model = null;
        if (!profile.Has(ItemField.Color)) item.Color = null;
        if (!profile.Has(ItemField.Size) || !profile.SizeSystems.Contains(item.SizeSystem ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            item.SizeSystem = null;
            item.SizeValue = null;
        }

        if (!profile.Has(ItemField.FunctionalStatus)) item.FunctionalStatus = InventoryFunctionalStatus.Unknown;
        if (!profile.Has(ItemField.CompletenessStatus)) item.CompletenessStatus = InventoryCompletenessStatus.Unknown;
        if (!profile.Has(ItemField.AuthenticityStatus)) item.AuthenticityStatus = InventoryAuthenticityStatus.NotAssessed;
        if (!profile.Has(ItemField.PackagingStatus)) item.PackagingStatus = InventoryPackagingStatus.NotApplicable;
        if (!profile.Has(ItemField.ManufacturerPartNumber)) item.ManufacturerPartNumber = null;
        if (!profile.Has(ItemField.SerialNumber)) item.SerialNumber = null;
        if (!profile.Has(ItemField.ManufactureYear)) item.ManufactureYear = null;
        if (!profile.Has(ItemField.CountryOfOrigin)) item.CountryOfOrigin = null;
        item.AttributesJson = KeepAttributes(item.AttributesJson, profile);
    }

    /// <summary>
    /// The attributes this profile defines, trimmed, with pick-list values held to their list. Anything else is dropped
    /// rather than kept as an unexplained key.
    /// </summary>
    public static string? KeepAttributes(string? attributesJson, ItemProfile profile)
    {
        if (string.IsNullOrWhiteSpace(attributesJson) || profile.Attributes.Count == 0)
        {
            return null;
        }

        JsonObject? source;
        try
        {
            source = JsonNode.Parse(attributesJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        JsonObject kept = [];
        foreach (ItemAttribute attribute in profile.Attributes)
        {
            string? value = source?[attribute.Key] is JsonValue node && node.TryGetValue(out string? text) ? text?.Trim() : null;
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            string? canonical = attribute.Options is null
                ? value
                : attribute.Options.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
            if (canonical is not null)
            {
                kept[attribute.Key] = canonical;
            }
        }

        return kept.Count == 0 ? null : kept.ToJsonString();
    }
}
