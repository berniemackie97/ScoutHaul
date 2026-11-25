using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

public sealed class ItemConditionScalesAndMeasurementsTests
{
    [Fact]
    public void Each_kind_of_item_has_its_own_condition_scale_drawn_from_the_standard_terms()
    {
        foreach (ItemProfile profile in ItemProfiles.All)
        {
            Assert.NotEmpty(profile.Conditions);
            Assert.All(profile.Conditions, condition => Assert.Contains(condition, ItemConditions.Terms));
            Assert.All(profile.Conditions, condition => Assert.True(ItemConditions.Hints.ContainsKey(condition), condition));
        }

        Assert.Contains("New with tags", ItemProfiles.For("Clothing").Conditions);
        Assert.DoesNotContain("For parts", ItemProfiles.For("Clothing").Conditions);
        Assert.Contains("For parts", ItemProfiles.For("Electronics").Conditions);
        Assert.DoesNotContain("New with tags", ItemProfiles.For("Electronics").Conditions);
        Assert.Empty(ItemProfiles.General.Conditions);
    }

    [Fact]
    public void Scales_keep_the_standard_best_first_order()
    {
        foreach (ItemProfile profile in ItemProfiles.All)
        {
            int[] positions = [.. profile.Conditions.Select(condition => ItemConditions.Terms.ToList().IndexOf(condition))];
            Assert.Equal(positions.Order(), positions);
        }
    }

    [Fact]
    public void Measurements_keep_only_the_profiles_keys_as_positive_numbers_with_a_unit()
    {
        ItemProfile clothing = ItemProfiles.For("Clothing");

        string? kept = ItemMeasurements.Keep("""{"pitToPit":"19.5","length":26,"sleeve":-3,"insole":10,"unit":"CM","junk":"x"}""", clothing);

        Assert.Equal("""{"unit":"cm","pitToPit":19.5,"length":26}""", kept);
    }

    [Theory]
    [InlineData("""{"pitToPit":0}""")]
    [InlineData("""{}""")]
    [InlineData("""[1,2]""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Nothing_usable_reads_as_no_measurements(string json) =>
        Assert.Null(ItemMeasurements.Keep(json, ItemProfiles.For("Clothing")));

    [Fact]
    public void A_kind_without_measurements_keeps_none_and_a_missing_unit_reads_as_inches()
    {
        Assert.Null(ItemMeasurements.Keep("""{"pitToPit":19}""", ItemProfiles.For("Electronics")));
        Assert.Equal("""{"unit":"in","insole":10.25}""", ItemMeasurements.Keep("""{"insole":10.25}""", ItemProfiles.For("Shoes")));
    }

    [Fact]
    public void Changing_an_item_to_a_kind_without_measurements_clears_them()
    {
        InventoryItem item = new() { Title = "Tee", Category = "Electronics", MeasurementsJson = """{"unit":"in","pitToPit":19}""" };

        ItemProfiles.ClearInapplicable(item);

        Assert.Null(item.MeasurementsJson);
    }
}
