namespace HaulScout.Domain;

/// <summary>What things are made of, per kind of item: a watch is not offered cotton, nor a shirt stainless steel.</summary>
public static class ItemMaterials
{
    /// <summary>A shell, a lining and a trim: three covers what a buyer filters by; the rest belongs in the description.</summary>
    public const int Max = 3;

    private static readonly IReadOnlyDictionary<string, string> OtherSpellings = new Dictionary<string, string>
    {
        ["Elastane"] = "Spandex",
        ["Lycra"] = "Spandex",
        ["Viscose"] = "Rayon",
        ["Tencel"] = "Lyocell",
        ["Polyamide"] = "Nylon",
        ["Poly"] = "Polyester",
        ["Merino"] = "Wool",
        ["Lambswool"] = "Wool",
        ["Sherpa"] = "Fleece",
        ["Jean"] = "Denim",
        ["Vegan leather"] = "Faux leather",
        ["PU leather"] = "Faux leather",
        ["Pleather"] = "Faux leather",
        ["Leatherette"] = "Faux leather",
        ["Genuine leather"] = "Leather",
        ["Lambskin"] = "Leather",
        ["Cowhide"] = "Leather",
        ["Calfskin"] = "Leather",
        ["Patent"] = "Patent leather",
        ["925"] = "Sterling silver",
        ["925 silver"] = "Sterling silver",
        ["10k"] = "Gold",
        ["14k"] = "Gold",
        ["18k"] = "Gold",
        ["Gold tone"] = "Gold plated",
        ["Silver tone"] = "Silver plated",
        ["Steel"] = "Stainless steel",
        ["Aluminium"] = "Aluminum",
        ["Silicon"] = "Silicone",
        ["Stoneware"] = "Ceramic",
        ["Earthenware"] = "Ceramic",
        ["Wicker"] = "Rattan",
        ["Oak"] = "Wood",
        ["Walnut"] = "Wood",
        ["Pine"] = "Wood",
        ["Teak"] = "Wood",
        ["Mahogany"] = "Wood",
    };

    public static readonly Vocabulary Clothing = new(
    [
        "Acrylic", "Alpaca", "Angora", "Bamboo", "Canvas", "Cashmere", "Chiffon", "Corduroy", "Cotton", "Denim", "Down",
        "Faux fur", "Faux leather", "Faux suede", "Flannel", "Fleece", "Fur", "Hemp", "Lace", "Leather", "Linen", "Lyocell",
        "Mesh", "Modal", "Mohair", "Nylon", "Polyester", "Rayon", "Satin", "Sequin", "Silk", "Spandex", "Suede", "Tweed",
        "Velvet", "Wool",
    ], OtherSpellings);

    public static readonly Vocabulary Footwear = new(
    [
        "Canvas", "Cork", "Faux fur", "Faux leather", "Faux suede", "Knit", "Leather", "Mesh", "Nubuck", "Patent leather",
        "Rubber", "Satin", "Shearling", "Suede", "Synthetic", "Textile", "Velvet", "Wool",
    ], OtherSpellings);

    public static readonly Vocabulary BagsAndJewelry = new(
    [
        "Brass", "Canvas", "Copper", "Crystal", "Denim", "Enamel", "Faux leather", "Glass", "Gold", "Gold plated", "Leather",
        "Nylon", "Pearl", "Platinum", "Plastic", "Resin", "Rose gold", "Silver", "Silver plated", "Stainless steel",
        "Sterling silver", "Straw", "Suede", "Titanium", "Velvet", "Wood",
    ], OtherSpellings);

    public static readonly Vocabulary Watches = new(
    [
        "Carbon fiber", "Ceramic", "Gold", "Gold plated", "Leather", "Plastic", "Platinum", "Resin", "Rose gold", "Rubber",
        "Silicone", "Silver", "Stainless steel", "Titanium",
    ], OtherSpellings);

    public static readonly Vocabulary Sporting = new(
    [
        "Aluminum", "Carbon fiber", "Composite", "Fiberglass", "Foam", "Leather", "Mesh", "Neoprene", "Nylon", "Plastic",
        "Polyester", "Rubber", "Stainless steel", "Titanium", "Wood",
    ], OtherSpellings);

    public static readonly Vocabulary Home = new(
    [
        "Brass", "Bronze", "Cast iron", "Ceramic", "Copper", "Cotton", "Crystal", "Glass", "Iron", "Leather", "Linen", "Marble",
        "Metal", "Pewter", "Plastic", "Porcelain", "Rattan", "Resin", "Silver", "Stainless steel", "Stone", "Velvet", "Wood",
        "Wool",
    ], OtherSpellings);
}
