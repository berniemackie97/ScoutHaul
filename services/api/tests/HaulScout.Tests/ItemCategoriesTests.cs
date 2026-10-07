using HaulScout.Domain;

namespace HaulScout.Tests;

/// <summary>A marketplace's category name, filed under the category it belongs to.</summary>
public class ItemCategoriesTests
{
    [Theory]
    [InlineData("Tops", "Clothing")]
    [InlineData("Hoodies & Sweatshirts", "Clothing")]
    [InlineData("Casual Button-Down Shirts", "Clothing")]
    [InlineData("T-Shirts", "Clothing")]
    [InlineData("CCG Individual Cards", "Collectibles")]
    [InlineData("Collectible Figures & Bobbleheads", "Collectibles")]
    [InlineData("LEGO (R) Bricks, Pieces & Parts", "Toys")]
    [InlineData("Other Kitchen Tools & Gadgets", "Home")]
    [InlineData("Controllers & Attachments", "Video Games")]
    [InlineData("Bath & Body", "Health & Beauty")]
    [InlineData("handbags", "Handbags")]
    [InlineData("Clothing, Shoes & Accessories", "Clothing")]
    [InlineData("Women > Shoes > Heels", "Shoes")]
    [InlineData("Women › Accessories › Football-NFL", "Handbags")]
    [InlineData("Football-NFL", null)]
    [InlineData("", null)]
    public void A_marketplace_category_is_filed_under_its_own(string marketplace, string? expected) =>
        Assert.Equal(expected, ItemCategories.Canonical(ItemCategories.Standard, marketplace));

    [Fact]
    public void Every_other_spelling_names_a_category_the_profiles_have()
    {
        Assert.All(ItemCategories.OtherSpellings.Values, category => Assert.Contains(category, ItemCategories.Terms));
        Assert.Contains(ItemCategories.Other, ItemCategories.Terms);
    }
}
