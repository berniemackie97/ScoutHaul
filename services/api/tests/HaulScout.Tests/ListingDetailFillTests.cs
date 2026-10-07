using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

/// <summary>
/// What a marketplace listing says about a thing, put on the item it is linked to. The rule that matters is the one
/// about not overwriting: an operator who typed something knows more than the listing does.
/// </summary>
public sealed class ListingDetailFillTests
{
    private static PlatformListing Listing() => new()
    {
        Platform = "Vinted",
        ExternalListingId = "9482228763",
        Title = "Carhartt Detroit Jacket",
        Description = "Worn twice, no flaws.",
        Brand = "Carhartt",
        Color = "Brown",
        Size = "M",
        Condition = "Very good",
        CategoryName = "Jackets",
    };

    [Fact]
    public void An_empty_item_takes_everything_the_listing_says()
    {
        InventoryItem item = new() { Title = "Jacket" };

        Assert.True(ListingDetailFill.Apply(item, Listing()));

        Assert.Equal("Worn twice, no flaws.", item.Description);
        Assert.Equal("Carhartt", item.Brand);
        Assert.Equal(["Brown"], item.Colors);
        Assert.Equal("M", item.SizeValue);
        Assert.Equal("Very good", item.Condition);
        Assert.Equal("Clothing", item.Category);
    }

    [Fact]
    public void What_a_person_wrote_is_left_exactly_as_they_wrote_it()
    {
        InventoryItem item = new()
        {
            Title = "Jacket",
            Description = "Small tear on the left cuff, priced accordingly.",
            Brand = "Carhartt WIP",
            Colors = ["Tan"],
        };

        bool changed = ListingDetailFill.Apply(item, Listing());

        Assert.True(changed);
        Assert.Equal("Small tear on the left cuff, priced accordingly.", item.Description);
        Assert.Equal("Carhartt WIP", item.Brand);
        Assert.Equal(["Tan"], item.Colors);
        // The blanks it did have are filled.
        Assert.Equal("M", item.SizeValue);
    }

    [Fact]
    public void A_listing_with_nothing_to_add_changes_nothing()
    {
        InventoryItem item = new() { Title = "Jacket", Brand = "Carhartt" };
        PlatformListing quiet = new() { Platform = "eBay", ExternalListingId = "1", Title = "Jacket" };

        Assert.False(ListingDetailFill.Apply(item, quiet));
        Assert.Equal("Carhartt", item.Brand);
    }

    [Fact]
    public void Whitespace_is_neither_stored_nor_counted_as_an_answer()
    {
        InventoryItem item = new() { Title = "Jacket" };
        PlatformListing listing = new() { Platform = "eBay", ExternalListingId = "1", Title = "Jacket", Brand = "  Nike  ", Color = "   " };

        Assert.True(ListingDetailFill.Apply(item, listing));

        Assert.Equal("Nike", item.Brand);
        // A colour of spaces is not a colour.
        Assert.Empty(item.Colors);
    }

    [Fact]
    public void A_second_listing_finds_the_blanks_already_filled()
    {
        // The ordinary case: one item listed on two marketplaces. Whichever is read first fills the blanks.
        InventoryItem item = new() { Title = "Jacket" };
        ListingDetailFill.Apply(item, Listing());

        PlatformListing ebay = new()
        {
            Platform = "eBay",
            ExternalListingId = "377296454991",
            Title = "Carhartt Jacket",
            Brand = "CARHARTT",
            Color = "Tan",
        };

        Assert.False(ListingDetailFill.Apply(item, ebay));
        Assert.Equal("Carhartt", item.Brand);
        Assert.Equal(["Brown"], item.Colors);
    }

    [Fact]
    public void A_listings_colors_arrive_in_the_standard_list()
    {
        InventoryItem item = new() { Title = "Scarf" };
        PlatformListing poshmark = new() { Platform = "Poshmark", ExternalListingId = "1", Title = "Scarf", Color = "Grey, Off-White" };

        Assert.True(ListingDetailFill.Apply(item, poshmark));
        Assert.Equal(["Gray", "Cream"], item.Colors);
    }
}
