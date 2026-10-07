using HaulScout.Application.Contracts;
using HaulScout.Domain;
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

    public static readonly InventoryReferenceOptionDto[] Colors = [.. ItemColors.Terms.Select(color => Option(color, color))];

    public static readonly InventoryReferenceOptionDto[] SizeSystems =
    [
        Option("US letter", "US Letter (S/M/L/XL)"),
        Option("US numeric", "US Numeric (general)"),
        Option("US women's numeric", "US Women's Numeric"),
        Option("US juniors numeric", "US Juniors Numeric"),
        Option("US women's plus", "US Women's Plus"),
        Option("US men's numeric", "US Men's Numeric / Chest"),
        Option("EU numeric", "EU Numeric"),
        Option("UK numeric", "UK Numeric"),
        Option("AU numeric", "AU Numeric"),
        Option("IT numeric", "Italian Numeric"),
        Option("FR numeric", "French Numeric"),
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

    // Curated brands tagged by category (ParentValue = category) — suggestions, not a closed list: any other brand is
    // typed in as itself. There is no "Other" brand; an unknown brand is left blank.
    public static readonly InventoryReferenceOptionDto[] Brands = BuildBrands();

    // Curated model/line/set options tagged by brand (ParentValue = brand). This is not meant to replace unique
    // identifiers, titles, serials, set numbers, or listing descriptions; it standardizes the broad model dimension.
    public static readonly InventoryReferenceOptionDto[] Models = BuildModels();

    private static InventoryReferenceOptionDto[] BuildBrands()
    {
        List<InventoryReferenceOptionDto> brands = [];
        B(brands, "Video Games", "Nintendo", "Sony", "Microsoft", "Sega", "Atari", "SNK", "Bandai Namco", "Capcom", "Square Enix", "Ubisoft", "Electronic Arts", "Activision", "Rockstar Games", "Konami", "Valve", "2K", "Bethesda", "Take-Two", "THQ Nordic", "Limited Run Games", "GameStop");
        B(brands, "Clothing", "Nike", "Adidas", "Lululemon", "The North Face", "Patagonia", "Levi's", "Carhartt", "Ralph Lauren", "Polo Ralph Lauren", "Tommy Hilfiger", "Champion", "Under Armour", "Columbia", "Gap", "Old Navy", "Zara", "H&M", "Supreme", "Gymshark", "Vineyard Vines", "Free People", "Anthropologie", "Gucci", "Louis Vuitton", "Burberry", "J.Crew", "Madewell", "American Eagle", "Abercrombie & Fitch", "Hollister", "Aritzia", "Urban Outfitters", "Banana Republic", "Talbots", "Ann Taylor", "Eileen Fisher", "Harley-Davidson", "Disney", "MLB", "NFL", "NBA", "NHL");
        B(brands, "Shoes", "Nike", "Jordan", "Adidas", "New Balance", "Vans", "Converse", "Dr. Martens", "Timberland", "Reebok", "Puma", "ASICS", "Birkenstock", "UGG", "Crocs", "Salomon", "Hoka", "On", "Clarks", "Sperry", "Brooks", "Saucony", "Merrell", "Keen", "Sorel", "Steve Madden", "Cole Haan", "Allen Edmonds");
        B(brands, "Handbags", "Coach", "Michael Kors", "Kate Spade", "Fossil", "Tory Burch", "Louis Vuitton", "Gucci", "Prada", "Chanel", "Dooney & Bourke", "Longchamp", "MCM", "Marc Jacobs", "Rebecca Minkoff", "Vera Bradley", "Tumi", "Herschel");
        B(brands, "Watches", "Seiko", "Casio", "Citizen", "Fossil", "Timex", "Bulova", "Invicta", "Garmin", "Apple", "Rolex", "Omega", "Tag Heuer", "Movado", "Tissot", "Hamilton", "Orient", "Swatch", "G-Shock");
        B(brands, "Jewelry", "Pandora", "Tiffany & Co.", "Swarovski", "David Yurman", "Kay", "James Avery", "Alex and Ani", "Kendra Scott", "Cartier", "Van Cleef & Arpels", "Brighton", "Silpada");
        B(brands, "Tech", "Apple", "Dell", "HP", "Lenovo", "ASUS", "Acer", "Microsoft", "Razer", "MSI", "Logitech", "Samsung", "LG", "Intel", "AMD", "NVIDIA", "Corsair", "Alienware", "Ubiquiti", "Netgear", "TP-Link", "Synology", "Western Digital", "Seagate");
        B(brands, "Phones", "Apple", "Samsung", "Google", "Motorola", "OnePlus", "LG", "Nokia", "OtterBox", "Spigen", "Case-Mate", "Anker", "Belkin");
        B(brands, "Cameras", "Canon", "Nikon", "Sony", "Fujifilm", "Panasonic", "Olympus", "Leica", "GoPro", "DJI", "Polaroid", "Kodak", "Pentax", "Sigma", "Tamron");
        B(brands, "Audio", "Bose", "Sony", "JBL", "Beats", "Sennheiser", "Sonos", "Apple", "Audio-Technica", "Marshall", "Ultimate Ears", "Anker", "Shure", "Yamaha", "Pioneer", "Denon", "Klipsch", "Harman Kardon", "Skullcandy");
        B(brands, "TV", "Samsung", "LG", "Sony", "Vizio", "TCL", "Hisense", "Roku", "Panasonic", "Philips", "Sharp", "Insignia");
        B(brands, "Electronics", "Apple", "Samsung", "Sony", "LG", "Anker", "Logitech", "Belkin", "Amazon", "Google", "Meta", "Ring", "Eero", "Philips Hue", "Nintendo", "Microsoft");
        B(brands, "Appliances", "KitchenAid", "Dyson", "Ninja", "Instant Pot", "Cuisinart", "Breville", "Vitamix", "Keurig", "Nespresso", "Shark", "Hamilton Beach", "Crock-Pot", "Bissell", "Hoover", "Miele", "iRobot", "Black+Decker");
        B(brands, "Toys", "LEGO", "Hasbro", "Mattel", "Funko", "Fisher-Price", "Playmobil", "Hot Wheels", "Nerf", "Barbie", "Bandai", "Melissa & Doug", "Squishmallows", "Spin Master", "Jakks Pacific", "Disney", "Nintendo", "Pokémon");
        B(brands, "Collectibles", "Pokémon", "Nintendo", "Panini", "Topps", "Upper Deck", "Wizards of the Coast", "Konami", "Funko", "Hallmark", "Bowman", "Magic: The Gathering", "Yu-Gi-Oh!", "Disney", "Marvel", "DC", "McFarlane Toys", "NECA", "Hot Wheels", "LEGO", "PSA", "CGC", "BGS");
        B(brands, "Books", "Penguin Random House", "HarperCollins", "Simon & Schuster", "Macmillan", "Scholastic", "DK", "Marvel", "DC");
        B(brands, "Media", "Disney", "Criterion", "Warner Bros.", "Universal", "Sony Pictures", "Paramount", "Nintendo", "PlayStation", "Xbox");
        B(brands, "Musical Instruments", "Fender", "Gibson", "Yamaha", "Roland", "Korg", "Ibanez", "Shure", "Epiphone", "Casio", "Boss", "Martin", "Taylor", "Squier", "Pearl", "Zildjian", "Alesis");
        B(brands, "Furniture", "IKEA", "West Elm", "Pottery Barn", "Ashley", "La-Z-Boy", "Herman Miller", "Steelcase", "Restoration Hardware");
        B(brands, "Decor", "Pottery Barn", "West Elm", "Anthropologie", "IKEA", "Crate & Barrel", "Yankee Candle", "Lenox", "Waterford");
        B(brands, "Home", "IKEA", "Pyrex", "CorningWare", "Le Creuset", "Lodge", "OXO", "KitchenAid", "Williams Sonoma", "Crate & Barrel", "Threshold");
        B(brands, "Tools", "DeWalt", "Milwaukee", "Makita", "Bosch", "Ryobi", "Craftsman", "Stanley", "Snap-on", "RIDGID", "Black+Decker", "Kobalt", "Husky", "Dremel", "Metabo", "Festool", "Hilti");
        B(brands, "Garden", "Stihl", "Husqvarna", "EGO", "Ryobi", "DeWalt", "Greenworks", "Toro", "Scotts", "Fiskars");
        B(brands, "Office", "Herman Miller", "Steelcase", "Staples", "HP", "Canon", "Brother", "Epson", "Fellowes", "Logitech");
        B(brands, "Crafts", "Cricut", "Silhouette", "Singer", "Brother", "Janome", "DMC", "Fiskars", "Michaels", "Sizzix");
        B(brands, "Sporting Goods", "Wilson", "Spalding", "Titleist", "Callaway", "TaylorMade", "Yeti", "Coleman", "Shimano", "Nike", "Adidas", "Rawlings", "Easton", "Mizuno", "Garmin", "The North Face", "Patagonia");
        B(brands, "Fitness", "Bowflex", "NordicTrack", "Peloton", "Rogue", "Theragun", "Fitbit", "Garmin", "Concept2", "Schwinn", "Bowmar", "Hydro Flask");
        B(brands, "Health & Beauty", "Dyson", "Shark", "Revlon", "Conair", "T3", "ghd", "Clinique", "MAC", "Estée Lauder", "Dior", "Chanel", "Fenty Beauty", "The Ordinary");
        B(brands, "Baby", "Graco", "Chicco", "Fisher-Price", "Britax", "Evenflo", "Medela", "Baby Brezza", "UPPAbaby", "Nuna", "Doona", "Cybex", "Ergobaby");
        B(brands, "Pet Supplies", "Kong", "PetSafe", "Furbo", "Chewy", "Blue Buffalo", "Ruffwear", "Kurgo", "Petmate", "Fluval");
        B(brands, "Automotive", "Bosch", "ACDelco", "Mobil 1", "K&N", "WeatherTech", "Garmin", "Michelin", "Goodyear", "Meguiar's", "Chemical Guys", "NOCO");
        B(brands, "Industrial", "3M", "Honeywell", "Fluke", "Grainger", "Klein Tools", "Milwaukee", "DeWalt", "Makita");
        B(brands, "Home", "OXO", "Pyrex", "Corelle", "Lodge", "Le Creuset", "Rubbermaid", "Tupperware", "Anchor Hocking", "Yeti", "Stanley", "Corningware", "Farberware");
        B(brands, "Furniture", "IKEA", "Ashley", "Herman Miller", "La-Z-Boy", "West Elm", "Pottery Barn", "Steelcase", "Wayfair", "Crate & Barrel");
        B(brands, "Decor", "Yankee Candle", "Bath & Body Works", "Threshold", "Hearth & Hand", "Anthropologie");
        B(brands, "Health & Beauty", "Dyson", "Olaplex", "CeraVe", "The Ordinary", "Drunk Elephant", "Philips", "Oral-B", "Braun", "Conair", "Revlon", "L'Oréal", "Estée Lauder");
        B(brands, "Garden", "Scotts", "Miracle-Gro", "Greenworks", "EGO", "Weber", "Traeger", "Sun Joe");
        B(brands, "Office", "HP", "Canon", "Brother", "Epson", "Logitech", "Herman Miller", "Fellowes", "Brother");
        B(brands, "Crafts", "Cricut", "Singer", "Brother", "Bernina", "Silhouette", "Sizzix");
        B(brands, "Books", "Scholastic", "Penguin Random House", "Marvel", "DC Comics", "LEGO");
        return brands
            .DistinctBy(brand => $"{brand.ParentValue}\u001f{brand.Value}")
            .ToArray();
    }

    private static void B(List<InventoryReferenceOptionDto> target, string category, params string[] names) =>
        target.AddRange(names.Select(name => new InventoryReferenceOptionDto(name, name, category, category)));

    private static InventoryReferenceOptionDto[] BuildModels()
    {
        List<InventoryReferenceOptionDto> models = [];
        M(models, "Nintendo", "NES", "SNES", "Nintendo 64", "GameCube", "Wii", "Wii U", "Switch", "Switch Lite", "Switch OLED", "Game Boy", "Game Boy Color", "Game Boy Advance", "Nintendo DS", "Nintendo 3DS", "Amiibo");
        M(models, "Sony", "PlayStation", "PlayStation 2", "PlayStation 3", "PlayStation 4", "PlayStation 5", "PSP", "PS Vita", "Walkman", "Alpha", "Cyber-shot", "Bravia");
        M(models, "Microsoft", "Xbox", "Xbox 360", "Xbox One", "Xbox Series S", "Xbox Series X", "Surface", "Zune");
        M(models, "Apple", "iPhone", "iPad", "MacBook", "MacBook Air", "MacBook Pro", "iMac", "Mac mini", "Apple Watch", "AirPods", "HomePod", "Apple TV");
        M(models, "Samsung", "Galaxy S", "Galaxy Note", "Galaxy Z Fold", "Galaxy Z Flip", "Galaxy Tab", "Galaxy Watch", "QLED", "The Frame");
        M(models, "Google", "Pixel", "Pixel Pro", "Pixel Watch", "Nest", "Chromecast");
        M(models, "Pokémon", "Base Set", "Jungle", "Fossil", "Team Rocket", "Gym Heroes", "Neo", "EX", "Diamond & Pearl", "Black & White", "XY", "Sun & Moon", "Sword & Shield", "Scarlet & Violet", "Japanese", "Promo", "Elite Trainer Box");
        M(models, "Magic: The Gathering", "Alpha", "Beta", "Unlimited", "Revised", "Commander", "Modern", "Standard", "Collector Booster", "Set Booster", "Draft Booster");
        M(models, "Yu-Gi-Oh!", "LOB", "MRD", "PSV", "Structure Deck", "Starter Deck", "Tin", "Booster Box");
        M(models, "LEGO", "Star Wars", "Technic", "City", "Creator", "Ideas", "Icons", "Harry Potter", "Marvel", "Ninjago", "Speed Champions", "Minifigures");
        M(models, "Nike", "Air Force 1", "Air Max", "Air Jordan", "Dunk", "Blazer", "Cortez", "Pegasus", "Vapormax");
        M(models, "Jordan", "Jordan 1", "Jordan 2", "Jordan 3", "Jordan 4", "Jordan 5", "Jordan 6", "Jordan 11", "Jordan 12", "Jordan Retro");
        M(models, "Adidas", "Ultraboost", "NMD", "Samba", "Gazelle", "Stan Smith", "Superstar", "Yeezy");
        M(models, "Coach", "Tabby", "Rogue", "Willow", "Pillow Tabby", "Swinger", "Court", "Legacy");
        M(models, "Louis Vuitton", "Neverfull", "Speedy", "Alma", "Pochette", "Keepall", "Noé", "Sarah Wallet");
        M(models, "Canon", "EOS", "Rebel", "PowerShot", "EF Lens", "RF Lens");
        M(models, "Nikon", "D Series", "Z Series", "Coolpix", "Nikkor Lens");
        M(models, "Fujifilm", "X Series", "Instax", "FinePix");
        M(models, "GoPro", "Hero", "Max", "Session");
        M(models, "DJI", "Mavic", "Mini", "Air", "Phantom", "Osmo");
        M(models, "DeWalt", "20V MAX", "FLEXVOLT", "Atomic", "XR");
        M(models, "Milwaukee", "M12", "M18", "Fuel", "Packout");
        M(models, "Makita", "LXT", "XGT", "CXT");

        return models
            .DistinctBy(model => $"{model.ParentValue}\u001f{model.Value}")
            .OrderBy(model => model.ParentValue)
            .ThenBy(model => model.Value)
            .ToArray();
    }

    private static void M(List<InventoryReferenceOptionDto> target, string brand, params string[] names) =>
        target.AddRange(names.Select(name => new InventoryReferenceOptionDto(name, name, ParentValue: brand)));

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
        List<InventoryReferenceOptionDto> sizes = [];
        Add(sizes, "US letter", ["XXS", "XS", "S", "M", "L", "XL", "2XL", "3XL", "4XL", "5XL", "6XL", "7XL", "8XL"]);
        Add(sizes, "US numeric", ["000", "00", .. Enumerable.Range(0, 61).Select(number => number.ToString())]);
        Add(sizes, "US women's numeric", ["000", "00", .. EvenNumbers(0, 40)]);
        Add(sizes, "US juniors numeric", ["0", .. OddNumbers(1, 31)]);
        Add(sizes, "US women's plus", [.. EvenNumbers(12, 44).Select(number => $"{number}W"), "1X", "2X", "3X", "4X", "5X", "6X"]);
        Add(sizes, "US men's numeric", Enumerable.Range(28, 43).Select(number => number.ToString()).ToArray());
        Add(sizes, "EU numeric", Enumerable.Range(28, 37).Select(number => number.ToString()).ToArray());
        Add(sizes, "UK numeric", Enumerable.Range(2, 39).Select(number => number.ToString()).ToArray());
        Add(sizes, "AU numeric", Enumerable.Range(2, 39).Select(number => number.ToString()).ToArray());
        Add(sizes, "IT numeric", Enumerable.Range(34, 33).Select(number => number.ToString()).ToArray());
        Add(sizes, "FR numeric", Enumerable.Range(30, 33).Select(number => number.ToString()).ToArray());
        Add(sizes, "Shoe US M", HalfSteps(4, 18));
        Add(sizes, "Shoe US W", HalfSteps(4, 16));
        Add(sizes, "Shoe US Youth", HalfSteps(1, 7));
        Add(sizes, "Shoe EU", Enumerable.Range(20, 29).Select(number => number.ToString()).ToArray());
        Add(sizes, "Shoe UK", HalfSteps(1, 14));
        Add(sizes, "Youth", ["Newborn", "0–3M", "3–6M", "6–9M", "9–12M", "12–18M", "18–24M", "2T", "3T", "4T", "5T", "6", "7", "8", "10", "12", "14", "16"]);
        Add(sizes, "One size", ["One size"]);

        string[] waists = Enumerable.Range(20, 51).Select(number => number.ToString()).ToArray();
        string[] inseams = Enumerable.Range(24, 17).Select(number => number.ToString()).ToArray();
        Add(sizes, "Waist/Inseam", waists.SelectMany(waist => inseams.Select(inseam => $"{waist}×{inseam}")).ToArray());
        return sizes.ToArray();
    }

    private static string[] HalfSteps(int from, int through) =>
        Enumerable.Range(from * 2, (through - from) * 2 + 1)
            .Select(value => value % 2 == 0 ? (value / 2).ToString() : $"{value / 2}.5")
            .ToArray();

    private static IEnumerable<string> EvenNumbers(int from, int through) =>
        Enumerable.Range(from, through - from + 1)
            .Where(number => number % 2 == 0)
            .Select(number => number.ToString());

    private static IEnumerable<string> OddNumbers(int from, int through) =>
        Enumerable.Range(from, through - from + 1)
            .Where(number => number % 2 != 0)
            .Select(number => number.ToString());

    private static void Add(List<InventoryReferenceOptionDto> target, string parent, IEnumerable<string> values) =>
        target.AddRange(values.Select(value => new InventoryReferenceOptionDto(value, value, ParentValue: parent)));

    private static InventoryReferenceOptionDto Option(string value, string label, string? group = null) =>
        new(value, label, group);
}
