using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Tests;

public sealed class ListingPriceAdvisorTests
{
    [Fact]
    public void Uses_compatible_raw_sold_comps_and_rejects_proxy_graded_and_outliers()
    {
        ListingPriceAdvice advice = ListingPriceAdvisor.Calculate(
            new GenerateListingRequest(
                "Gardevoir & Sylveon GX 130/214",
                Brand: "Pokemon",
                Category: "Collectibles",
                Condition: "Near Mint",
                AttributesJson: """{"set":"SM Unbroken Bonds","cardNumber":"130/214","variant":"TAG TEAM Holo","grading":"Raw"}"""),
            Estimate(
            [
                Sold(59.99m, "Pokemon Gardevoir & Sylveon GX TAG TEAM 130/214 SM Unbroken Bonds Holo NEAR MINT"),
                Sold(56m, "Pokemon TCG Gardevoir & Sylveon GX Holo Card 130/214 Unbroken Bonds NM"),
                Sold(65m, "Pokemon Gardevoir & Sylveon GX Unbroken Bonds Holo Card 130/214"),
                Sold(0.93m, "Basic Gardevoir & Sylveon GX Tag Team HP 260 Gold Foil Fan Art Card"),
                Sold(179m, "2019 Pokemon Sun Moon Unbroken Bonds #130 Gardevoir & Sylveon GX PSA 9"),
                Sold(79.99m, "Gardevoir & Sylveon GX Tag Team 130/214 Sm-Unbroken Bonds Rare Holo - NM"),
                Sold(60m, "Gardevoir & Sylveon GX 130/214 Holo Unbroken Bonds"),
            ]));

        Assert.Equal(64.99m, advice.SuggestedListPrice);
        Assert.Equal(5, advice.PricingComps.Count);
        Assert.DoesNotContain(advice.PricingComps, comp => comp.Price == 0.93m);
        Assert.DoesNotContain(advice.PricingComps, comp => comp.Note?.Contains("PSA", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Japanese_item_uses_japanese_comps_not_english_raw_card_comps()
    {
        ListingPriceAdvice advice = ListingPriceAdvisor.Calculate(
            new GenerateListingRequest(
                "Japanese Gardevoir & Sylveon GX 130/214",
                Brand: "Pokemon",
                Category: "Collectibles",
                AttributesJson: """{"language":"Japanese","cardNumber":"130/214"}"""),
            Estimate(
            [
                Sold(34m, "Japanese Gardevoir & Sylveon GX 130/214 Pokemon Card"),
                Sold(36m, "JPN Pokemon Gardevoir & Sylveon GX 130/214"),
                Sold(65m, "English Gardevoir & Sylveon GX 130/214 Unbroken Bonds NM"),
            ]));

        Assert.Equal(34.99m, advice.SuggestedListPrice);
        Assert.Equal(2, advice.PricingComps.Count);
        Assert.All(advice.PricingComps, comp => Assert.Contains("j", comp.Note!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Graded_item_uses_matching_grader_and_grade_not_raw_or_other_grade()
    {
        ListingPriceAdvice advice = ListingPriceAdvisor.Calculate(
            new GenerateListingRequest(
                "PSA 9 Gardevoir & Sylveon GX 130/214",
                Brand: "Pokemon",
                Category: "Collectibles",
                AttributesJson: """{"gradingCompany":"PSA","grade":"9","cardNumber":"130/214"}"""),
            Estimate(
            [
                Sold(180m, "PSA 9 Gardevoir & Sylveon GX 130/214 Unbroken Bonds"),
                Sold(62m, "Raw Gardevoir & Sylveon GX 130/214 Unbroken Bonds"),
                Sold(320m, "PSA 10 Gardevoir & Sylveon GX 130/214 Unbroken Bonds"),
                Sold(155m, "CGC 9 Gardevoir & Sylveon GX 130/214 Unbroken Bonds"),
            ]));

        Assert.Equal(179.99m, advice.SuggestedListPrice);
        Assert.Single(advice.PricingComps);
        Assert.Contains("PSA 9", advice.PricingComps[0].Note);
    }

    [Fact]
    public void Lot_or_collection_uses_lot_comps_not_single_item_comps()
    {
        ListingPriceAdvice advice = ListingPriceAdvisor.Calculate(
            new GenerateListingRequest(
                "Pokemon GX card lot collection",
                Brand: "Pokemon",
                Category: "Collectibles",
                Notes: "Lot of 12 assorted GX cards"),
            Estimate(
            [
                Sold(120m, "Pokemon GX card lot collection 12 cards"),
                Sold(118m, "Bundle set of 12 Pokemon GX cards"),
                Sold(15m, "Single Pokemon GX card"),
            ]));

        Assert.Equal(118.99m, advice.SuggestedListPrice);
        Assert.Equal(2, advice.PricingComps.Count);
        Assert.DoesNotContain(advice.PricingComps, comp => comp.Note?.Contains("Single", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static ResaleEstimate Estimate(IReadOnlyList<ResaleComp> comps) =>
        new(10m, 100m, "test", PriceRangeType: "moderate", Comps: comps);

    private static ResaleComp Sold(decimal price, string note) =>
        new(price, "sold", null, $"eBay sold — {note}", "https://www.ebay.com/itm/test");
}
