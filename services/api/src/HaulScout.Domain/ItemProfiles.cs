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

/// <summary>
/// A detail stored in the item's attributes. With a vocabulary it is a pick list, of up to <see cref="MaxValues"/>
/// terms; without one, free text.
/// </summary>
public sealed record ItemAttribute(string Key, string Label, Vocabulary? Choices = null, int MaxValues = 1)
{
    public IReadOnlyList<string>? Options => Choices?.Terms;

    public bool IsList => Choices is not null && MaxValues > 1;
}

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

    /// <summary>The conditions this kind of item is described in, best first; empty when every condition applies.</summary>
    public IReadOnlyList<string> Conditions => ItemConditions.ScaleFor(Key);

    /// <summary>The flat-lay measurements buyers of this kind of item filter on; empty when none apply.</summary>
    public IReadOnlyList<ItemMeasurement> Measurements => ItemMeasurements.For(Key);
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

    private static readonly ItemAttribute Department = new(
        "department",
        "Department",
        new Vocabulary(["Women", "Men", "Unisex", "Girls", "Boys", "Baby"], new Dictionary<string, string> { ["Womens"] = "Women", ["Mens"] = "Men" }));

    private static ItemAttribute Material(Vocabulary materials) => new("material", "Material", materials, ItemMaterials.Max);

    private static ItemAttribute PickOne(string key, string label, params string[] options) => new(key, label, new Vocabulary(options));

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
        new("apparel", "Clothing", ["Clothing"], [Color, Size, Authenticity()], [Department, Material(ItemMaterials.Clothing)], ClothingSizes),
        new("baby", "Baby", ["Baby"], [Model(), Color, Size, Completeness(), Packaging(ListingReadiness.Optional)], [], ["Youth", "One size"]),
        new("footwear", "Shoes", ["Shoes"], [Model(), Color, Size, Authenticity(), Packaging()], [Department, Material(ItemMaterials.Footwear)], ShoeSizes),
        new("accessories", "Bags & jewelry", ["Handbags", "Jewelry"], [Model(), Color, Authenticity(), Packaging()], [Material(ItemMaterials.BagsAndJewelry)], []),
        new(
            "watches",
            "Watches",
            ["Watches"],
            [Model(), Color, Serial, Year, Functional(ListingReadiness.Recommended), Completeness(ListingReadiness.Optional), Authenticity(), Packaging()],
            [Material(ItemMaterials.Watches)],
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
            [PickOne("region", "Region", "NTSC-U (North America)", "PAL (Europe)", "NTSC-J (Japan)", "Region free")],
            [],
            BrandLabel: "Console maker or publisher"),
        new(
            "collectibles",
            "Collectibles & toys",
            ["Collectibles", "Toys"],
            [Model("Set or series"), Year, Completeness(), Authenticity(), Packaging(), Origin],
            [PickOne("grader", "Grading company", "PSA", "BGS", "CGC", "SGC", "Ungraded"), new ItemAttribute("grade", "Grade")],
            []),
        new(
            "media",
            "Books & media",
            ["Books", "Media"],
            [Year, Completeness(), Packaging()],
            [
                new ItemAttribute("creator", "Author or artist"),
                PickOne("format", "Format", "Hardcover", "Paperback", "Vinyl", "CD", "Cassette", "DVD", "Blu-ray", "VHS", "Digital code"),
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
        new("sporting", "Sporting goods", ["Sporting Goods"], [Model(), Color, Authenticity(), Completeness(ListingReadiness.Optional)], [Material(ItemMaterials.Sporting)], []),
        new(
            "home",
            "Home & lifestyle",
            ["Furniture", "Decor", "Home", "Crafts", "Health & Beauty", "Pet Supplies", "Other"],
            [Model(), Color, Completeness(ListingReadiness.Optional), Packaging(ListingReadiness.Optional), Origin],
            [Material(ItemMaterials.Home)],
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
        if (!profile.Has(ItemField.Color)) item.Colors.Clear();
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
        item.MeasurementsJson = ItemMeasurements.Keep(item.MeasurementsJson, profile);
    }

    /// <summary>
    /// The attributes this profile defines, trimmed, with pick-list values held to their vocabulary — one term, or a
    /// list of up to the attribute's limit. Anything else is dropped rather than kept as an unexplained key.
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
            IReadOnlyList<string?> values = Texts(source?[attribute.Key]);
            if (attribute.IsList)
            {
                IReadOnlyList<string> terms = attribute.Choices!.CanonicalList(values, attribute.MaxValues);
                if (terms.Count > 0)
                {
                    kept[attribute.Key] = new JsonArray([.. terms.Select(term => JsonValue.Create(term))]);
                }

                continue;
            }

            string? value = values.FirstOrDefault()?.Trim();
            string? canonical = attribute.Choices is null ? (string.IsNullOrEmpty(value) ? null : value) : attribute.Choices.Canonical(value);
            if (canonical is not null)
            {
                kept[attribute.Key] = canonical;
            }
        }

        return kept.Count == 0 ? null : kept.ToJsonString();
    }

    /// <summary>The text a stored attribute holds, whether written as one string or, for a list, as an array.</summary>
    private static IReadOnlyList<string?> Texts(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue(out string? text) => [text],
        JsonArray array => [.. array.Select(entry => entry is JsonValue value && value.TryGetValue(out string? text) ? text : null)],
        _ => [],
    };
}
