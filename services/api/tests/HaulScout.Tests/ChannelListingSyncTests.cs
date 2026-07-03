using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

public class ChannelListingSyncTests
{
    private static ChannelListing Listing(string channel, ChannelListingStatus status) => new()
    {
        Status = status,
        ChannelAccount = new ChannelAccount { Channel = channel, DisplayName = channel },
    };

    [Fact]
    public void Sale_marks_its_channel_sold_and_flags_the_rest_for_delist()
    {
        var ebay = Listing("eBay", ChannelListingStatus.Active);
        var posh = Listing("Poshmark", ChannelListingStatus.Active);
        var depop = Listing("Depop", ChannelListingStatus.Delisted);

        ChannelListingSync.ApplySale([ebay, posh, depop], "eBay", DateTimeOffset.UtcNow);

        Assert.Equal(ChannelListingStatus.Sold, ebay.Status);
        Assert.NotNull(ebay.SoldAt);
        Assert.Equal(ChannelListingStatus.PendingDelist, posh.Status);
        // Already delisted stays delisted — only live listings are flagged.
        Assert.Equal(ChannelListingStatus.Delisted, depop.Status);
    }

    [Fact]
    public void Sale_on_unknown_channel_still_flags_live_listings_for_delist()
    {
        var ebay = Listing("eBay", ChannelListingStatus.Active);
        ChannelListingSync.ApplySale([ebay], "Craigslist", DateTimeOffset.UtcNow);
        Assert.Equal(ChannelListingStatus.PendingDelist, ebay.Status);
    }
}
