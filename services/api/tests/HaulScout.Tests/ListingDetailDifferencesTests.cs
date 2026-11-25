using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

public sealed class ListingDetailDifferencesTests
{
    private static PlatformListing Listing() => new()
    {
        Platform = "eBay",
        ExternalListingId = "1",
        Title = "Tee",
        Brand = "Levi Strauss & Co.",
        Color = "NAVY",
        Size = "M",
        Condition = "Pre-owned - Excellent",
        CategoryName = "Tops",
    };

    private static InventoryItem Item() => new()
    {
        Title = "Tee",
        Category = "Clothing",
        Brand = "Levi's",
        Colors = ["Navy"],
        Condition = "New without tags",
        SizeSystem = "US letter",
        SizeValue = "L",
    };

    [Fact]
    public void Only_details_that_still_differ_after_reading_the_listing_in_our_terms_show()
    {
        IReadOnlyList<ListingDetailDifference> differences = ListingDetailDifferences.Find(Item(), Listing());

        Assert.Equal([ListingDetailField.Condition, ListingDetailField.Brand, ListingDetailField.Size], differences.Select(entry => entry.Field));
        ListingDetailDifference condition = differences.Single(entry => entry.Field == ListingDetailField.Condition);
        Assert.Equal("New without tags", condition.Current);
        Assert.Equal("Excellent", condition.Incoming);
        Assert.Equal("Pre-owned - Excellent", condition.Raw);
        Assert.Equal("M (US letter)", differences.Single(entry => entry.Field == ListingDetailField.Size).Incoming);
    }

    [Fact]
    public void A_blank_on_the_item_is_not_a_difference_since_the_sync_fills_it()
    {
        InventoryItem item = Item();
        item.Brand = null;

        Assert.DoesNotContain(ListingDetailDifferences.Find(item, Listing()), entry => entry.Field == ListingDetailField.Brand);
    }

    [Fact]
    public void Taking_a_value_sets_it_and_leaves_the_rest()
    {
        InventoryItem item = Item();

        Assert.True(ListingDetailDifferences.Apply(item, Listing(), ListingDetailField.Size));

        Assert.Equal("M", item.SizeValue);
        Assert.Equal("Levi's", item.Brand);
        Assert.DoesNotContain(ListingDetailDifferences.Find(item, Listing()), entry => entry.Field == ListingDetailField.Size);
    }

    [Fact]
    public void Taking_a_new_category_clears_what_the_new_kind_lacks()
    {
        InventoryItem item = Item();
        PlatformListing listing = Listing();
        listing.CategoryName = "Headphones";

        Assert.True(ListingDetailDifferences.Apply(item, listing, ListingDetailField.Category));

        Assert.Equal("Audio", item.Category);
        Assert.Null(item.SizeValue);
    }

    [Fact]
    public void Nothing_to_take_is_refused()
    {
        PlatformListing listing = Listing();
        listing.Brand = "  ";

        Assert.False(ListingDetailDifferences.Apply(Item(), listing, ListingDetailField.Brand));
    }
}
