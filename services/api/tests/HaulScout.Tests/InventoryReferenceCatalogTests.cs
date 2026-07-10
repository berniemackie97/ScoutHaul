using HaulScout.Application.Services;

namespace HaulScout.Tests;

public class InventoryReferenceCatalogTests
{
    [Fact]
    public void Brands_are_scoped_to_their_category()
    {
        List<string> videoGameBrands = InventoryReferenceCatalog.Brands
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

    [Fact]
    public void Clothing_size_catalog_covers_non_us_numeric_and_extended_ranges()
    {
        Assert.Contains(InventoryReferenceCatalog.SizeSystems, system => system.Value == "EU numeric");
        Assert.Contains(InventoryReferenceCatalog.SizeSystems, system => system.Value == "UK numeric");
        Assert.Contains(InventoryReferenceCatalog.SizeSystems, system => system.Value == "US women's plus");

        Assert.Contains(InventoryReferenceCatalog.Sizes, size => size.ParentValue == "US numeric" && size.Value == "60");
        Assert.Contains(InventoryReferenceCatalog.Sizes, size => size.ParentValue == "US women's plus" && size.Value == "44W");
        Assert.Contains(InventoryReferenceCatalog.Sizes, size => size.ParentValue == "Waist/Inseam" && size.Value == "70×40");
        Assert.Contains(InventoryReferenceCatalog.Sizes, size => size.ParentValue == "EU numeric" && size.Value == "64");
        Assert.Contains(InventoryReferenceCatalog.Sizes, size => size.ParentValue == "UK numeric" && size.Value == "40");
    }
}
