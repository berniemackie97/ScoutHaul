using HaulScout.Domain;

namespace HaulScout.Tests;

/// <summary>Conditions and sizes as marketplaces write them, read in the editor's terms.</summary>
public class ItemConditionsAndSizesTests
{
    [Theory]
    [InlineData("ug", "Good")]
    [InlineData("ULN", "Like new")]
    [InlineData("NWT", "New with tags")]
    [InlineData("Pre-owned - Excellent", "Excellent")]
    [InlineData("Excellent condition", "Excellent")]
    [InlineData("Satisfactory", "Fair")]
    [InlineData("Used - needs minor repair", "Fair")]
    [InlineData("Used", "Good")]
    [InlineData("Ungraded", null)]
    public void A_marketplace_condition_is_read_as_a_listed_one(string written, string? expected) =>
        Assert.Equal(expected, ItemConditions.Standard.Canonical(written));

    [Theory]
    [InlineData("L / US 12-14", "Clothing", "US letter", "L")]
    [InlineData("XXL", "Clothing", "US letter", "2XL")]
    [InlineData("O/S", "Clothing", "One size", "One size")]
    [InlineData("14", "Clothing", "US numeric", "14")]
    [InlineData("7", "Shoes", null, null)]
    [InlineData("M", "Video Games", null, null)]
    [InlineData("W36", "Clothing", null, null)]
    public void A_size_gets_a_system_only_when_the_category_says_which(string written, string category, string? system, string? value)
    {
        (string System, string Value)? read = ItemSizes.Read(written, ItemProfiles.For(category));

        Assert.Equal(system, read?.System);
        Assert.Equal(value, read?.Value);
    }
}
