using HaulScout.Application.Contracts;
using System.Globalization;

namespace HaulScout.Application.Services;

public static class InventoryReferenceCatalog
{
    public static readonly InventoryReferenceOptionDto[] Categories =
    [
        Option("Clothing", "Clothing", "Apparel"),
        Option("Shoes", "Shoes", "Apparel"),
        Option("Handbags", "Handbags & Accessories", "Apparel"),
        Option("Jewelry", "Jewelry", "Apparel"),
        Option("Watches", "Watches", "Apparel"),
        Option("Tech", "Tech & Computers", "Electronics"),
        Option("Phones", "Cell Phones & Accessories", "Electronics"),
        Option("Cameras", "Cameras & Photography", "Electronics"),
        Option("Audio", "Audio & Headphones", "Electronics"),
        Option("TV", "TV & Home Theater", "Electronics"),
        Option("Electronics", "Electronics", "Electronics"),
        Option("Appliances", "Appliances", "Electronics"),
        Option("Video Games", "Video Games", "Entertainment"),
        Option("Toys", "Toys & Games", "Entertainment"),
        Option("Collectibles", "Collectibles", "Entertainment"),
        Option("Books", "Books", "Entertainment"),
        Option("Media", "Media (DVD, Vinyl, etc.)", "Entertainment"),
        Option("Musical Instruments", "Musical Instruments", "Entertainment"),
        Option("Furniture", "Furniture", "Home"),
        Option("Decor", "Decor & Art", "Home"),
        Option("Home", "Home & Kitchen", "Home"),
        Option("Tools", "Tools & Hardware", "Home"),
        Option("Garden", "Garden & Outdoor", "Home"),
        Option("Office", "Office Equipment & Supplies", "Home"),
        Option("Crafts", "Crafts & Hobbies", "Home"),
        Option("Sporting Goods", "Sporting Goods", "Lifestyle"),
        Option("Fitness", "Fitness Equipment", "Lifestyle"),
        Option("Health & Beauty", "Health & Beauty", "Lifestyle"),
        Option("Baby", "Baby", "Lifestyle"),
        Option("Pet Supplies", "Pet Supplies", "Lifestyle"),
        Option("Automotive", "Automotive", "Automotive & Industrial"),
        Option("Industrial", "Business & Industrial", "Automotive & Industrial"),
        Option("Other", "Other", "Other"),
    ];

    public static readonly InventoryReferenceOptionDto[] Conditions =
    [
        Option("Factory sealed", "Factory sealed"),
        Option("New", "New"),
        Option("New with tags", "New with tags"),
        Option("New without tags", "New without tags"),
        Option("Open box", "Open box"),
        Option("Refurbished", "Refurbished"),
        Option("Excellent", "Excellent"),
        Option("Good", "Good"),
        Option("Fair", "Fair"),
        Option("Poor", "Poor"),
        Option("For parts", "For parts / not working"),
    ];

    public static readonly InventoryReferenceOptionDto[] Colors =
    [
        Option("Black", "Black"), Option("White", "White"), Option("Gray", "Gray"),
        Option("Silver", "Silver"), Option("Red", "Red"), Option("Orange", "Orange"),
        Option("Yellow", "Yellow"), Option("Green", "Green"), Option("Blue", "Blue"),
        Option("Purple", "Purple"), Option("Pink", "Pink"), Option("Brown", "Brown"),
        Option("Beige", "Beige"), Option("Cream", "Cream"), Option("Navy", "Navy"),
        Option("Teal", "Teal"), Option("Maroon", "Maroon"), Option("Gold", "Gold"),
        Option("Rose gold", "Rose gold"), Option("Bronze", "Bronze"), Option("Natural", "Natural"),
        Option("Multicolor", "Multicolor"), Option("Patterned", "Patterned"),
        Option("Clear", "Clear"), Option("Other", "Other"),
    ];

    public static readonly InventoryReferenceOptionDto[] SizeSystems =
    [
        Option("US letter", "US Letter (S/M/L/XL)"),
        Option("US numeric", "US Numeric"),
        Option("Waist/Inseam", "Waist × Inseam"),
        Option("Shoe US M", "Shoe US Men's"),
        Option("Shoe US W", "Shoe US Women's"),
        Option("Shoe US Youth", "Shoe US Youth"),
        Option("Shoe EU", "Shoe EU"),
        Option("Shoe UK", "Shoe UK"),
        Option("Youth", "Youth / Kids"),
        Option("One size", "One Size"),
    ];

    public static readonly InventoryReferenceOptionDto[] Sizes = BuildSizes();

    // Curated brands tagged by category (ParentValue = category). The reference service also folds in any
    // brands already typed into inventory as universal options, so this list guides without limiting.
    public static readonly InventoryReferenceOptionDto[] Brands = BuildBrands();

    private static InventoryReferenceOptionDto[] BuildBrands()
    {
        var brands = new List<InventoryReferenceOptionDto>();
        B(brands, "Video Games", "Nintendo", "Sony", "Microsoft", "Sega", "Atari", "SNK", "Bandai Namco", "Capcom", "Square Enix", "Ubisoft", "Electronic Arts", "Activision", "Rockstar Games", "Konami", "Valve", "2K");
        B(brands, "Clothing", "Nike", "Adidas", "Lululemon", "The North Face", "Patagonia", "Levi's", "Carhartt", "Ralph Lauren", "Polo Ralph Lauren", "Tommy Hilfiger", "Champion", "Under Armour", "Columbia", "Gap", "Old Navy", "Zara", "H&M", "Supreme", "Gymshark", "Vineyard Vines", "Free People", "Anthropologie", "Gucci", "Louis Vuitton", "Burberry");
        B(brands, "Shoes", "Nike", "Jordan", "Adidas", "New Balance", "Vans", "Converse", "Dr. Martens", "Timberland", "Reebok", "Puma", "ASICS", "Birkenstock", "UGG", "Crocs", "Salomon", "Hoka", "On", "Clarks", "Sperry");
        B(brands, "Handbags", "Coach", "Michael Kors", "Kate Spade", "Fossil", "Tory Burch", "Louis Vuitton", "Gucci", "Prada", "Chanel", "Dooney & Bourke", "Longchamp");
        B(brands, "Watches", "Seiko", "Casio", "Citizen", "Fossil", "Timex", "Bulova", "Invicta", "Garmin", "Apple", "Rolex", "Omega", "Tag Heuer", "Movado");
        B(brands, "Jewelry", "Pandora", "Tiffany & Co.", "Swarovski", "David Yurman", "Kay", "James Avery", "Alex and Ani");
        B(brands, "Tech", "Apple", "Dell", "HP", "Lenovo", "ASUS", "Acer", "Microsoft", "Razer", "MSI", "Logitech", "Samsung", "LG", "Intel", "AMD", "NVIDIA");
        B(brands, "Phones", "Apple", "Samsung", "Google", "Motorola", "OnePlus", "LG", "Nokia", "OtterBox", "Spigen");
        B(brands, "Cameras", "Canon", "Nikon", "Sony", "Fujifilm", "Panasonic", "Olympus", "Leica", "GoPro", "DJI", "Polaroid");
        B(brands, "Audio", "Bose", "Sony", "JBL", "Beats", "Sennheiser", "Sonos", "Apple", "Audio-Technica", "Marshall", "Ultimate Ears", "Anker");
        B(brands, "TV", "Samsung", "LG", "Sony", "Vizio", "TCL", "Hisense", "Roku", "Panasonic");
        B(brands, "Electronics", "Apple", "Samsung", "Sony", "LG", "Anker", "Logitech", "Belkin", "Amazon");
        B(brands, "Appliances", "KitchenAid", "Dyson", "Ninja", "Instant Pot", "Cuisinart", "Breville", "Vitamix", "Keurig", "Nespresso", "Shark", "Hamilton Beach", "Crock-Pot");
        B(brands, "Toys", "LEGO", "Hasbro", "Mattel", "Funko", "Fisher-Price", "Playmobil", "Hot Wheels", "Nerf", "Barbie", "Bandai", "Melissa & Doug", "Squishmallows");
        B(brands, "Collectibles", "Pokémon", "Panini", "Topps", "Upper Deck", "Wizards of the Coast", "Konami", "Funko", "Hallmark", "Bowman");
        B(brands, "Musical Instruments", "Fender", "Gibson", "Yamaha", "Roland", "Korg", "Ibanez", "Shure", "Epiphone", "Casio", "Boss");
        B(brands, "Tools", "DeWalt", "Milwaukee", "Makita", "Bosch", "Ryobi", "Craftsman", "Stanley", "Snap-on", "RIDGID", "Black+Decker", "Kobalt");
        B(brands, "Sporting Goods", "Wilson", "Spalding", "Titleist", "Callaway", "TaylorMade", "Yeti", "Coleman", "Shimano", "Nike", "Adidas");
        B(brands, "Fitness", "Bowflex", "NordicTrack", "Peloton", "Rogue", "Theragun", "Fitbit", "Garmin");
        B(brands, "Baby", "Graco", "Chicco", "Fisher-Price", "Britax", "Evenflo", "Medela", "Baby Brezza");
        B(brands, "Pet Supplies", "Kong", "PetSafe", "Furbo", "Chewy", "Blue Buffalo");
        B(brands, "Automotive", "Bosch", "ACDelco", "Mobil 1", "K&N", "WeatherTech", "Garmin");
        return brands
            .DistinctBy(brand => $"{brand.ParentValue}{brand.Value}")
            .ToArray();
    }

    private static void B(List<InventoryReferenceOptionDto> target, string category, params string[] names) =>
        target.AddRange(names.Select(name => new InventoryReferenceOptionDto(name, name, category, category)));

    public static readonly InventoryReferenceOptionDto[] Countries = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name))
        .DistinctBy(region => region.TwoLetterISORegionName)
        .OrderBy(region => region.EnglishName)
        .Select(region => new InventoryReferenceOptionDto(region.TwoLetterISORegionName, region.EnglishName))
        .ToArray();

    public static string? CanonicalValue(IEnumerable<InventoryReferenceOptionDto> options, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : options.FirstOrDefault(option => string.Equals(option.Value, value.Trim(), StringComparison.OrdinalIgnoreCase))?.Value;

    private static InventoryReferenceOptionDto[] BuildSizes()
    {
        var sizes = new List<InventoryReferenceOptionDto>();
        Add(sizes, "US letter", ["XXS", "XS", "S", "M", "L", "XL", "2XL", "3XL", "4XL", "5XL"]);
        Add(sizes, "US numeric", Enumerable.Range(0, 21).Select(number => number.ToString()).ToArray());
        Add(sizes, "Shoe US M", HalfSteps(4, 18));
        Add(sizes, "Shoe US W", HalfSteps(4, 16));
        Add(sizes, "Shoe US Youth", HalfSteps(1, 7));
        Add(sizes, "Shoe EU", Enumerable.Range(20, 29).Select(number => number.ToString()).ToArray());
        Add(sizes, "Shoe UK", HalfSteps(1, 14));
        Add(sizes, "Youth", ["Newborn", "0–3M", "3–6M", "6–9M", "9–12M", "12–18M", "18–24M", "2T", "3T", "4T", "5T", "6", "7", "8", "10", "12", "14", "16"]);
        Add(sizes, "One size", ["One size"]);

        string[] waists = ["26", "28", "30", "31", "32", "33", "34", "36", "38", "40", "42", "44", "46", "48"];
        string[] inseams = ["28", "30", "32", "34", "36"];
        Add(sizes, "Waist/Inseam", waists.SelectMany(waist => inseams.Select(inseam => $"{waist}×{inseam}")).ToArray());
        return sizes.ToArray();
    }

    private static string[] HalfSteps(int from, int through) =>
        Enumerable.Range(from * 2, (through - from) * 2 + 1)
            .Select(value => value % 2 == 0 ? (value / 2).ToString() : $"{value / 2}.5")
            .ToArray();

    private static void Add(List<InventoryReferenceOptionDto> target, string parent, IEnumerable<string> values) =>
        target.AddRange(values.Select(value => new InventoryReferenceOptionDto(value, value, ParentValue: parent)));

    private static InventoryReferenceOptionDto Option(string value, string label, string? group = null) =>
        new(value, label, group);
}
