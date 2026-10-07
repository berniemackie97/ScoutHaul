using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

/// <summary>What survives when two records of one item are folded together, as pure rules with no database.</summary>
public class InventoryMergeTests
{
    private static InventoryItem Item(string title = "Nintendo 64 console") => new() { OrganizationId = Guid.NewGuid(), Title = title };

    private static ChannelListing Listing(ChannelListingStatus status, string? url = null, DateTimeOffset? synced = null) =>
        new() { Status = status, ExternalUrl = url, LastSyncedAt = synced };

    [Fact]
    public void The_kept_items_answers_stand_and_the_duplicate_fills_only_its_blanks()
    {
        InventoryItem kept = Item();
        kept.Brand = "Nintendo";
        kept.PurchasePrice = 40m;
        InventoryItem duplicate = Item("N64 console grey");
        duplicate.Brand = "Other";
        duplicate.PurchasePrice = 15m;
        duplicate.Colors = ["Gray"];
        duplicate.EstimatedSalePrice = 120m;

        InventoryMerge.FillBlanks(kept, duplicate);

        Assert.Equal("Nintendo 64 console", kept.Title);
        Assert.Equal("Nintendo", kept.Brand);
        Assert.Equal(40m, kept.PurchasePrice);
        Assert.Equal(["Gray"], kept.Colors);
        Assert.Equal(120m, kept.EstimatedSalePrice);
    }

    [Fact]
    public void A_size_and_its_system_travel_together()
    {
        InventoryItem kept = Item();
        kept.SizeValue = "M";
        InventoryItem duplicate = Item();
        duplicate.SizeSystem = "US";
        duplicate.SizeValue = "L";

        InventoryMerge.FillBlanks(kept, duplicate);

        Assert.Equal(("US", "L"), (kept.SizeSystem, kept.SizeValue));
    }

    [Fact]
    public void Notes_from_both_are_kept_once()
    {
        InventoryItem kept = Item();
        kept.Notes = "Bought at the Saturday sale.";
        InventoryItem duplicate = Item();
        duplicate.Notes = "Imported from eBay order 12-345.";
        InventoryItem same = Item();
        same.Notes = "Bought at the Saturday sale.";

        InventoryMerge.FillBlanks(kept, duplicate);
        InventoryMerge.FillBlanks(kept, same);

        Assert.Equal("Bought at the Saturday sale.\n\nImported from eBay order 12-345.", kept.Notes);
    }

    [Fact]
    public void The_earliest_purchase_and_listing_dates_win()
    {
        DateTimeOffset early = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        InventoryItem kept = Item();
        kept.PurchasedAt = early.AddDays(10);
        InventoryItem duplicate = Item();
        duplicate.PurchasedAt = early;
        duplicate.ListedAt = early.AddDays(2);

        InventoryMerge.FillBlanks(kept, duplicate);

        Assert.Equal(early, kept.PurchasedAt);
        Assert.Equal(early.AddDays(2), kept.ListedAt);
    }

    [Theory]
    [InlineData(InventoryStatus.Listed, InventoryStatus.Sold, InventoryStatus.Sold)]
    [InlineData(InventoryStatus.Shipped, InventoryStatus.Listed, InventoryStatus.Shipped)]
    [InlineData(InventoryStatus.Draft, InventoryStatus.ReadyToList, InventoryStatus.ReadyToList)]
    [InlineData(InventoryStatus.Listed, InventoryStatus.Disposed, InventoryStatus.Listed)]
    [InlineData(InventoryStatus.Listed, InventoryStatus.Donated, InventoryStatus.Listed)]
    public void The_merged_item_is_at_the_further_stage_unless_the_duplicate_was_thrown_away(InventoryStatus kept, InventoryStatus duplicate, InventoryStatus expected) =>
        Assert.Equal(expected, InventoryMerge.MergedStatus(kept, duplicate));

    [Fact]
    public void A_sold_or_live_listing_beats_one_that_is_not()
    {
        ChannelListing delisted = Listing(ChannelListingStatus.Delisted, "https://example.com/a");
        ChannelListing active = Listing(ChannelListingStatus.Active);
        ChannelListing sold = Listing(ChannelListingStatus.Sold);

        Assert.Same(active, InventoryMerge.Preferred(delisted, active));
        Assert.Same(sold, InventoryMerge.Preferred(active, sold));
    }

    [Fact]
    public void Between_equal_listings_the_one_with_a_link_then_the_freshest_wins()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ChannelListing linked = Listing(ChannelListingStatus.Active, "https://example.com/a", now.AddDays(-5));
        ChannelListing unlinked = Listing(ChannelListingStatus.Active, synced: now);
        ChannelListing fresher = Listing(ChannelListingStatus.Active, "https://example.com/b", now);

        Assert.Same(linked, InventoryMerge.Preferred(linked, unlinked));
        Assert.Same(fresher, InventoryMerge.Preferred(linked, fresher));
        Assert.Same(linked, InventoryMerge.Preferred(linked, Listing(ChannelListingStatus.Active, "https://example.com/c")));
    }
}
