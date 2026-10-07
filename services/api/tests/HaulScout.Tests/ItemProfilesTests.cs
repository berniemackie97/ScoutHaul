using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

public class ItemProfilesTests
{
    [Fact]
    public void Every_category_belongs_to_exactly_one_profile()
    {
        string[] categories = HaulScout.Application.Services.InventoryReferenceCatalog.Categories.Select(option => option.Value).ToArray();
        string[] profiled = ItemProfiles.All.SelectMany(profile => profile.Categories).ToArray();

        Assert.Empty(categories.Except(profiled, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(profiled.Length, profiled.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_profile_size_system_exists_in_the_catalog()
    {
        HashSet<string> systems = HaulScout.Application.Services.InventoryReferenceCatalog.SizeSystems
            .Select(option => option.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(ItemProfiles.All.SelectMany(profile => profile.SizeSystems), system => Assert.Contains(system, systems));
    }

    [Fact]
    public void A_video_game_keeps_what_a_game_has_and_loses_a_shoe_size()
    {
        InventoryItem item = new()
        {
            Title = "Super Mario 64",
            Category = "Video Games",
            Model = "Nintendo 64",
            SizeSystem = "Shoe US M",
            SizeValue = "10",
            FunctionalStatus = InventoryFunctionalStatus.FullyFunctional,
            AuthenticityStatus = InventoryAuthenticityStatus.Authentic,
            AttributesJson = """{"region":"ntsc-u (north america)","department":"Men"}""",
        };

        ItemProfiles.ClearInapplicable(item);

        Assert.Equal("Nintendo 64", item.Model);
        Assert.Equal(InventoryFunctionalStatus.FullyFunctional, item.FunctionalStatus);
        Assert.Null(item.SizeSystem);
        Assert.Null(item.SizeValue);
        Assert.Equal(InventoryAuthenticityStatus.NotAssessed, item.AuthenticityStatus);
        Assert.Equal("""{"region":"NTSC-U (North America)"}""", item.AttributesJson);
    }

    [Fact]
    public void A_shirt_drops_a_shoe_size_system_and_a_pick_list_value_off_its_list()
    {
        InventoryItem item = new()
        {
            Title = "Flannel",
            Category = "Clothing",
            SizeSystem = "Shoe US M",
            SizeValue = "10",
            SerialNumber = "X1",
            AttributesJson = """{"department":"Martian","material":" Wool "}""",
        };

        ItemProfiles.ClearInapplicable(item);

        Assert.Null(item.SizeSystem);
        Assert.Null(item.SerialNumber);
        Assert.Equal("""{"material":["Wool"]}""", item.AttributesJson);
    }

    [Fact]
    public void Materials_written_as_one_phrase_are_kept_as_listed_terms_up_to_the_limit()
    {
        InventoryItem item = new()
        {
            Title = "Blouse",
            Category = "Clothing",
            AttributesJson = """{"material":"Silk/polyester, 5% elastane, viscose lining"}""",
        };

        ItemProfiles.ClearInapplicable(item);

        Assert.Equal("""{"material":["Silk","Polyester","Spandex"]}""", item.AttributesJson);
    }

    [Fact]
    public void A_watch_is_not_made_of_cotton()
    {
        InventoryItem item = new() { Title = "Seiko", Category = "Watches", AttributesJson = """{"material":["Cotton","stainless steel"]}""" };

        ItemProfiles.ClearInapplicable(item);

        Assert.Equal("""{"material":["Stainless steel"]}""", item.AttributesJson);
    }

    [Fact]
    public void An_item_without_a_category_has_only_the_common_fields()
    {
        ItemProfile profile = ItemProfiles.For(null);

        Assert.Same(ItemProfiles.General, profile);
        Assert.Empty(profile.Fields);
    }
}
