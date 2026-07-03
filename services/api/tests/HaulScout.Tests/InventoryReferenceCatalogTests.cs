using HaulScout.Application.Services;

namespace HaulScout.Tests;

public class InventoryReferenceCatalogTests
{
    [Fact]
    public void Brands_are_scoped_to_their_category()
    {
        var videoGameBrands = InventoryReferenceCatalog.Brands
            .Where(brand => brand.ParentValue == "Video Games")
            .Select(brand => brand.Value)
            .ToList();

        Assert.Contains("Nintendo", videoGameBrands);
        Assert.Contains("Sega", videoGameBrands);
        // The reported bug: apparel brands must not surface under Video Games.
        Assert.DoesNotContain("Lululemon", videoGameBrands);
        Assert.DoesNotContain("Nike", videoGameBrands);
    }

    [Fact]
    public void Every_brand_carries_its_category_parent()
    {
        Assert.All(InventoryReferenceCatalog.Brands, brand =>
        {
            Assert.False(string.IsNullOrWhiteSpace(brand.ParentValue));
            // Parent must be a real category value.
            Assert.Contains(InventoryReferenceCatalog.Categories, category => category.Value == brand.ParentValue);
        });
    }
}
