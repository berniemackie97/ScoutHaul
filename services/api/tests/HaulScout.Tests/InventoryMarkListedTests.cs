using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

/// <summary>
/// Three paths put an item on sale — a manual listing, the storefront, and a marketplace channel —
/// and each used to decide for itself what that meant for the item's own status. The channel path
/// decided nothing, so an item live on eBay still read as "ready to list".
/// </summary>
public sealed class InventoryMarkListedTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static InventoryItem Item(InventoryStatus status, DateTimeOffset? listedAt = null) =>
        new() { Title = "Brass Lamp", Status = status, ListedAt = listedAt };

    [Theory]
    [InlineData(InventoryStatus.Draft)]
    [InlineData(InventoryStatus.Researching)]
    [InlineData(InventoryStatus.Watchlist)]
    [InlineData(InventoryStatus.Purchased)]
    [InlineData(InventoryStatus.NeedsCleaning)]
    [InlineData(InventoryStatus.NeedsTesting)]
    [InlineData(InventoryStatus.NeedsRepair)]
    [InlineData(InventoryStatus.NeedsPhotos)]
    [InlineData(InventoryStatus.ReadyToList)]
    public void Advances_an_item_that_has_not_been_listed_yet(InventoryStatus status)
    {
        InventoryItem item = Item(status);

        item.MarkListed(Now);

        Assert.Equal(InventoryStatus.Listed, item.Status);
        Assert.Equal(Now, item.ListedAt);
    }

    [Theory]
    [InlineData(InventoryStatus.Sold)]
    [InlineData(InventoryStatus.Packed)]
    [InlineData(InventoryStatus.Shipped)]
    [InlineData(InventoryStatus.Delivered)]
    [InlineData(InventoryStatus.Returned)]
    [InlineData(InventoryStatus.Donated)]
    [InlineData(InventoryStatus.Disposed)]
    public void Leaves_an_item_that_is_already_past_listing_alone(InventoryStatus status)
    {
        // Publishing a second channel must not walk a sold item back to Listed and erase that it went.
        InventoryItem item = Item(status);

        item.MarkListed(Now);

        Assert.Equal(status, item.Status);
    }

    [Fact]
    public void Leaves_a_bundle_component_alone()
    {
        // A component is not listed on its own; the bundle is what goes on sale.
        InventoryItem item = Item(InventoryStatus.Bundled);

        item.MarkListed(Now);

        Assert.Equal(InventoryStatus.Bundled, item.Status);
    }

    [Fact]
    public void Keeps_the_first_time_it_went_on_sale()
    {
        // Listing to a second channel is not a new listing date.
        DateTimeOffset first = Now.AddDays(-30);
        InventoryItem item = Item(InventoryStatus.Listed, first);

        item.MarkListed(Now);

        Assert.Equal(first, item.ListedAt);
    }

    [Fact]
    public void Records_a_listing_date_for_an_item_that_had_none()
    {
        InventoryItem item = Item(InventoryStatus.ReadyToList);

        item.MarkListed(Now);

        Assert.Equal(Now, item.ListedAt);
    }

    [Fact]
    public void Listing_twice_is_the_same_as_listing_once()
    {
        InventoryItem item = Item(InventoryStatus.ReadyToList);

        item.MarkListed(Now);
        item.MarkListed(Now.AddDays(1));

        Assert.Equal(InventoryStatus.Listed, item.Status);
        Assert.Equal(Now, item.ListedAt);
    }

    [Fact]
    public void The_rule_agrees_with_the_lifecycle_ordering()
    {
        foreach (InventoryStatus status in InventoryLifecycle.Order)
        {
            bool expected = status != InventoryStatus.Bundled
                && InventoryLifecycle.Rank(status) < InventoryLifecycle.Rank(InventoryStatus.Listed);

            Assert.Equal(expected, InventoryLifecycle.ShouldAdvanceToListed(status));
        }
    }
}
