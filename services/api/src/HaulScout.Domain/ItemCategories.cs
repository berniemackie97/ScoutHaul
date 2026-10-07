namespace HaulScout.Domain;

/// <summary>
/// The categories items are filed under — the ones the item profiles name — and how marketplaces name the same
/// things. A synced listing arrives as "Tops" or "CCG Individual Cards"; filed as Clothing or Collectibles it gets
/// the fields that kind of item has, and lists, filters and reports see one name.
/// </summary>
public static class ItemCategories
{
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> Terms = [.. ItemProfiles.All.SelectMany(profile => profile.Categories).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Marketplace category names, as single words or phrases, and the category each belongs to.</summary>
    public static readonly IReadOnlyDictionary<string, string> OtherSpellings = Spellings(
        ("Clothing", [
            "Clothes", "Apparel", "Tops", "Shirts", "T-Shirts", "Tees", "Blouses", "Polos", "Sweaters", "Cardigans",
            "Hoodies", "Sweatshirts", "Jackets", "Coats", "Vests", "Blazers", "Outerwear", "Dresses", "Skirts", "Jeans",
            "Pants", "Trousers", "Shorts", "Leggings", "Jumpsuits", "Rompers", "Overalls", "Suits", "Swimwear", "Swim",
            "Sleepwear", "Robes", "Pajamas", "Lingerie", "Intimates", "Activewear", "Uniforms", "Clothing, Shoes & Accessories",
        ]),
        ("Shoes", ["Footwear", "Sneakers", "Boots", "Heels", "Pumps", "Sandals", "Flats", "Loafers", "Slippers", "Wedges", "Mules", "Clogs", "Oxfords"]),
        ("Handbags", ["Bags", "Handbag", "Purses", "Backpacks", "Totes", "Clutches", "Wallets", "Crossbody", "Satchels", "Accessories", "Hats", "Belts", "Scarves", "Sunglasses"]),
        ("Jewelry", ["Jewellery", "Necklaces", "Rings", "Earrings", "Bracelets", "Brooches", "Pendants"]),
        ("Watches", ["Watch", "Wristwatches"]),
        ("Collectibles", ["Collectible", "Figures", "Figurines", "Bobbleheads", "Statues", "Memorabilia", "Trading Cards", "Cards", "CCG", "TCG", "Coins", "Stamps", "Autographs", "Pins"]),
        ("Toys", ["Toy", "LEGO", "Dolls", "Plush", "Action Figures", "Puzzles", "Board Games"]),
        ("Video Games", ["Video Game", "Consoles", "Controllers", "Gaming"]),
        ("Home", ["Kitchen", "Kitchenware", "Mugs", "Cups", "Dinnerware", "Tableware", "Glassware", "Cookware", "Bedding", "Bath", "Housewares"]),
        ("Decor", ["Art", "Wall Art", "Vases", "Candles", "Frames", "Rugs"]),
        ("Furniture", ["Chairs", "Tables", "Desks", "Shelves"]),
        ("Tech", ["Computers", "Computer", "Laptops", "Tablets"]),
        ("Phones", ["Cell Phones", "Smartphones"]),
        ("Audio", ["Headphones", "Speakers", "Earbuds"]),
        ("Cameras", ["Camera", "Lenses", "Photography"]),
        ("Health & Beauty", ["Bath & Body", "Beauty", "Makeup", "Skincare", "Fragrance", "Fragrances", "Cosmetics", "Hair Care"]),
        ("Sporting Goods", ["Sports", "Golf", "Fishing", "Camping", "Cycling"]),
        ("Fitness", ["Exercise", "Yoga"]),
        ("Books", ["Book", "Magazines", "Comics", "Textbooks"]),
        ("Media", ["DVDs", "DVD", "Vinyl", "Records", "CDs", "Movies"]),
        ("Pet Supplies", ["Pet", "Pets"]),
        ("Crafts", ["Craft", "Sewing", "Yarn"]),
        ("Tools", ["Tool", "Hardware"]));

    public static readonly Vocabulary Standard = From(Terms);

    /// <summary>The categories an organization files under — the list above, with its own added or removed.</summary>
    public static Vocabulary From(IEnumerable<string> organizationCategories) => new(organizationCategories, OtherSpellings);

    /// <summary>
    /// The category a marketplace's category names, read from the leaf of a path ("Women › Tops & Blouses") toward its
    /// root, or null when none of it is recognised.
    /// </summary>
    public static string? Canonical(Vocabulary categories, string? marketplaceCategory) =>
        (marketplaceCategory ?? string.Empty)
            .Split(['>', '›', '»'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()
            .Select(categories.Canonical)
            .FirstOrDefault(category => category is not null);

    private static Dictionary<string, string> Spellings(params (string Category, string[] Names)[] groups) =>
        groups
            .SelectMany(group => group.Names.Select(name => (name, group.Category)))
            .ToDictionary(entry => entry.name, entry => entry.Category, StringComparer.OrdinalIgnoreCase);
}
