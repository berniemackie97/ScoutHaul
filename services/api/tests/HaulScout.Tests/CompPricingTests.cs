using HaulScout.Application.Appraisal;

namespace HaulScout.Tests;

public sealed class CompPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly ItemFacts Headphones = Item("Sony WH-1000XM5 Wireless Headphones", brand: "Sony", model: "WH-1000XM5", condition: "Used");

    [Fact]
    public void Prices_from_matching_sales_with_strong_confidence_when_they_agree_and_are_recent()
    {
        PriceResult result = CompPricing.Price(Headphones,
        [
            Sold(40m, daysAgo: 5), Sold(42m, daysAgo: 8), Sold(45m, daysAgo: 12), Sold(48m, daysAgo: 15), Sold(50m, daysAgo: 20),
        ], estimate: null, Now);

        Assert.Equal(PriceBasis.Sales, result.Basis);
        Assert.Equal(47.99m, result.List);
        Assert.Equal(41.99m, result.QuickSale);
        Assert.Equal(49.99m, result.High);
        Assert.Equal(ConfidenceLevel.Strong, result.Level);
        Assert.Contains("5 matching sales.", result.Reasons);
        Assert.All(result.Comps, comp => Assert.True(comp.Included));
    }

    [Fact]
    public void A_sale_far_from_the_rest_is_left_out_as_an_outlier()
    {
        PriceResult result = CompPricing.Price(Headphones,
            [Sold(40m), Sold(42m), Sold(45m), Sold(48m), Sold(300m)], estimate: null, Now);

        AssessedComp outlier = Assert.Single(result.Comps, comp => !comp.Included);
        Assert.Equal(300m, outlier.Comp.Price);
        Assert.Equal("price outlier", outlier.ExcludedBecause);
        Assert.True(result.List < 50m);
    }

    [Fact]
    public void Prices_within_half_again_of_the_median_survive_even_when_the_rest_are_identical()
    {
        PriceResult result = CompPricing.Price(Headphones,
            [Sold(40m), Sold(40m), Sold(40m), Sold(55m)], estimate: null, Now);

        Assert.All(result.Comps, comp => Assert.True(comp.Included));
    }

    [Fact]
    public void Another_product_a_lot_or_a_graded_copy_is_not_a_comp()
    {
        ItemFacts card = Item("Charizard 4/102 Base Set Holo", brand: "Pokemon", condition: "Used");

        PriceResult result = CompPricing.Price(card,
        [
            Sold(320m, title: "Pokemon Charizard 4/102 Base Set Holo Rare"),
            Sold(2400m, title: "Pokemon Charizard 4/102 Base Set Holo PSA 9"),
            Sold(150m, title: "Lot of 20 Pokemon Base Set cards Charizard 4/102 included"),
            Sold(90m, title: "Pokemon Blastoise 2/102 Base Set Holo"),
        ], estimate: null, Now);

        Assert.Equal(320m, Assert.Single(result.Comps, comp => comp.Included).Comp.Price);
        Assert.Contains(result.Comps, comp => comp.ExcludedBecause == "graded comp for raw/ungraded item");
        Assert.Contains(result.Comps, comp => comp.ExcludedBecause == "lot/collection comp for single item");
        Assert.Contains(result.Comps, comp => comp.ExcludedBecause == "identifier mismatch");
    }

    [Fact]
    public void A_comp_naming_a_different_product_is_left_out()
    {
        PriceResult result = CompPricing.Price(Headphones,
            [Sold(180m, title: "Bose QuietComfort 45 Wireless Headphones"), Sold(210m)], estimate: null, Now);

        Assert.Equal("different item", Assert.Single(result.Comps, comp => !comp.Included).ExcludedBecause);
    }

    [Fact]
    public void A_neighbouring_model_is_not_a_comp_but_the_same_model_written_differently_is()
    {
        PriceResult result = CompPricing.Price(Headphones,
        [
            Sold(150m, title: "Sony WH-1000XM4 Wireless Headphones"),
            Sold(210m, title: "Sony WH1000XM5 Headphones Black"),
        ], estimate: null, Now);

        Assert.Equal("different model", Assert.Single(result.Comps, comp => !comp.Included).ExcludedBecause);
        Assert.Equal(210m, Assert.Single(result.Comps, comp => comp.Included).Comp.Price);
    }

    [Fact]
    public void A_new_item_is_not_priced_from_used_or_broken_sales()
    {
        ItemFacts sealedItem = Headphones with { Condition = "New in box" };

        PriceResult result = CompPricing.Price(sealedItem,
        [
            Sold(260m, condition: "New"),
            Sold(170m, condition: "Pre-owned"),
            Sold(40m, condition: "For parts or not working"),
        ], estimate: null, Now);

        Assert.Equal(260m, Assert.Single(result.Comps, comp => comp.Included).Comp.Price);
        Assert.Contains(result.Comps, comp => comp.ExcludedBecause == "used condition");
        Assert.Contains(result.Comps, comp => comp.ExcludedBecause == "sold for parts");
    }

    [Fact]
    public void Listings_alone_price_under_the_cheaper_asks_and_never_read_as_strong()
    {
        PriceResult result = CompPricing.Price(Headphones,
            [Asking(220m), Asking(240m), Asking(250m), Asking(260m), Asking(275m)], estimate: null, Now);

        Assert.Equal(PriceBasis.Asking, result.Basis);
        Assert.Equal(239.99m, result.List);
        Assert.True(result.Confidence <= 45);
        Assert.Contains(result.Reasons, reason => reason.StartsWith("No matching sales. Priced under the cheaper", StringComparison.Ordinal));
    }

    [Fact]
    public void Sales_win_over_listings_and_the_listings_are_mentioned()
    {
        PriceResult result = CompPricing.Price(Headphones,
            [Sold(200m), Sold(210m), Asking(260m)], estimate: null, Now);

        Assert.Equal(PriceBasis.Sales, result.Basis);
        Assert.Contains("1 current listing from $260.", result.Reasons);
    }

    [Fact]
    public void A_model_estimate_is_the_last_resort_and_is_capped_as_a_guess()
    {
        PriceResult result = CompPricing.Price(Headphones, [],
            new ModelEstimate(120m, 180m, "Typical used price.", "high"), Now);

        Assert.Equal(PriceBasis.Estimate, result.Basis);
        Assert.Equal(149.99m, result.List);
        Assert.Equal(119.99m, result.QuickSale);
        Assert.True(result.Confidence <= 25);
        Assert.Equal(ConfidenceLevel.Thin, result.Level);
    }

    [Fact]
    public void Nothing_known_gives_no_price_rather_than_a_placeholder()
    {
        PriceResult result = CompPricing.Price(Headphones, [], estimate: null, Now);

        Assert.Equal(PriceBasis.None, result.Basis);
        Assert.Null(result.List);
        Assert.Equal(0, result.Confidence);
        Assert.Equal(ConfidenceLevel.None, result.Level);
    }

    [Fact]
    public void Web_search_sales_count_for_less_and_say_so()
    {
        PriceResult verified = CompPricing.Price(Headphones, [Sold(200m), Sold(205m), Sold(210m)], estimate: null, Now);
        PriceResult unverified = CompPricing.Price(Headphones,
            [Sold(200m, verified: false), Sold(205m, verified: false), Sold(210m, verified: false)], estimate: null, Now);

        Assert.True(unverified.Confidence < verified.Confidence);
        Assert.Contains("3 of 3 found by web search and not checked against the listing.", unverified.Reasons);
    }

    [Fact]
    public void The_business_s_own_sales_are_named_and_weigh_more()
    {
        PriceResult result = CompPricing.Price(Headphones,
        [
            new Comp(CompKind.YourSale, "eBay", "Sony WH-1000XM5 Headphones Black", 190m, 0m, "Good", null, Now.AddDays(-30), true),
            Sold(150m),
        ], estimate: null, Now);

        Assert.Contains("2 matching sales, 1 of them yours.", result.Reasons);
        Assert.True(result.List > 170m);
    }

    [Fact]
    public void An_uncertain_identification_costs_confidence_and_says_why()
    {
        List<Comp> comps = [Sold(200m), Sold(205m), Sold(210m)];
        PriceResult certain = CompPricing.Price(Headphones, comps, estimate: null, Now);
        PriceResult guessed = CompPricing.Price(Headphones with { Certainty = IdentificationCertainty.Low }, comps, estimate: null, Now);

        Assert.Equal(certain.Confidence - 20, guessed.Confidence);
        Assert.Contains(guessed.Reasons, reason => reason.StartsWith("Identification is a best guess", StringComparison.Ordinal));
    }

    [Fact]
    public void A_price_guide_prices_a_used_item_from_its_used_figure_not_sealed_or_box_only()
    {
        ItemFacts game = Item("Super Mario 64 Nintendo 64", brand: "Nintendo", condition: null);

        PriceResult result = CompPricing.Price(game,
        [
            Guide(32m, "Loose"), Guide(95m, "Complete in box"), Guide(900m, "New sealed"), Guide(40m, "Box only"),
        ], estimate: null, Now);

        Assert.Equal(PriceBasis.Guide, result.Basis);
        Assert.Equal(31.99m, result.List);
        Assert.Equal(94.99m, result.High);
        Assert.Contains(result.Comps, comp => comp.ExcludedBecause == "new condition");
        Assert.Contains(result.Comps, comp => comp.ExcludedBecause == "part of the item only");
    }

    [Theory]
    [InlineData("Like new", ConditionBand.Used)]
    [InlineData("NWT", ConditionBand.New)]
    [InlineData("New without tags", ConditionBand.New)]
    [InlineData("Pre-owned", ConditionBand.Used)]
    [InlineData("For parts or not working", ConditionBand.ForParts)]
    [InlineData("Complete in box", ConditionBand.Used)]
    [InlineData("", ConditionBand.Unknown)]
    public void Conditions_fall_into_bands(string condition, ConditionBand band)
    {
        Assert.Equal(band, CompMatching.BandOf(condition));
    }

    [Theory]
    [InlineData("Air Jordan 1 Lost and Found", true)]
    [InlineData("Nike Air Max 90 Infrared", false)]
    [InlineData("Jordan", false)]
    public void A_terse_comp_title_can_still_name_a_long_item(string compTitle, bool same)
    {
        IReadOnlyList<string> item = CompMatching.Tokens("Nike Air Jordan 1 Retro High OG Lost and Found 2022 Mens Size 10");

        Assert.Equal(same, CompMatching.NamesSameItem(item, CompMatching.Tokens(compTitle)));
    }

    private static ItemFacts Item(string title, string? brand = null, string? model = null, string? condition = null) =>
        new(title, brand, model, Category: null, Color: null, condition, Identifiers: [], IdentificationCertainty.High);

    private static Comp Sold(decimal price, string title = "Sony WH-1000XM5 Wireless Noise Canceling Headphones",
        int daysAgo = 10, string? condition = "Pre-owned", bool verified = true) =>
        new(CompKind.Sold, "eBay", title, price, null, condition, null, Now.AddDays(-daysAgo), verified);

    private static Comp Asking(decimal price) =>
        new(CompKind.Asking, "eBay", "Sony WH-1000XM5 Wireless Headphones", price, null, "Pre-owned", null, null, true);

    private static Comp Guide(decimal price, string condition) =>
        new(CompKind.Guide, "PriceCharting", "Super Mario 64 (Nintendo 64)", price, null, condition, null, null, true);
}
