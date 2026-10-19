using HaulScout.Application.Services;

namespace HaulScout.Tests;

public sealed class ResaleSearchHintBuilderTests
{
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
