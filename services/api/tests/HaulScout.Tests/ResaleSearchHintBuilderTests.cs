using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Tests;

public sealed class ResaleSearchHintBuilderTests
{
    [Fact]
    public void Builds_ebay_sold_completed_url_from_listing_facts()
    {
        ResaleSearchHints hints = ResaleSearchHintBuilder.FromListingRequest(
            new GenerateListingRequest(
                "Gardevoir & Sylveon GX 130/214",
                Brand: "Pokemon",
                Category: "Collectibles",
                Condition: "Near Mint",
                AttributesJson: """{"set":"SM Unbroken Bonds","cardNumber":"130/214","variant":"TAG TEAM Holo","language":"English"}"""));

        Assert.Contains("Pokemon", hints.SearchQuery);
        Assert.Contains("Gardevoir & Sylveon GX 130/214", hints.SearchQuery);
        Assert.Contains("SM Unbroken Bonds", hints.SearchQuery);
        Assert.Contains("TAG TEAM Holo", hints.SearchQuery);
        Assert.Contains("Near Mint", hints.SearchQuery);
        Assert.StartsWith("https://www.ebay.com/sch/i.html?", hints.EbaySoldCompletedUrl);
        Assert.Contains("LH_Sold=1", hints.EbaySoldCompletedUrl);
        Assert.Contains("LH_Complete=1", hints.EbaySoldCompletedUrl);
        Assert.Contains("_sop=13", hints.EbaySoldCompletedUrl);
        Assert.Contains("130%2F214", hints.EbaySoldCompletedUrl);
    }

    [Fact]
    public void Builds_query_for_graded_and_language_specific_cards()
    {
        ResaleSearchHints hints = ResaleSearchHintBuilder.FromListingRequest(
            new GenerateListingRequest(
                "Gardevoir & Sylveon GX 130/214",
                Brand: "Pokemon",
                Category: "Collectibles",
                AttributesJson: """{"language":"Japanese","gradingCompany":"PSA","grade":"9","cardNumber":"130/214"}"""));

        Assert.Contains("Japanese", hints.SearchQuery);
        Assert.Contains("PSA", hints.SearchQuery);
        Assert.Contains("9", hints.SearchQuery);
        Assert.Contains("130/214", hints.SearchQuery);
        Assert.Contains("LH_Sold=1", hints.EbaySoldCompletedUrl);
    }

    [Fact]
    public void Builds_sold_completed_url_from_plain_query()
    {
        ResaleSearchHints hints = ResaleSearchHintBuilder.FromQuery("Sony WH-1000XM4 black");

        Assert.Equal("Sony WH-1000XM4 black", hints.SearchQuery);
        Assert.Equal(
            "https://www.ebay.com/sch/i.html?_nkw=Sony%20WH-1000XM4%20black&LH_Sold=1&LH_Complete=1&_sop=13",
            hints.EbaySoldCompletedUrl);
    }
}
