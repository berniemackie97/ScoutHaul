using HaulScout.Domain;

namespace HaulScout.Tests;

public class BundledSaleAllocatorTests
{
    private static BundledSaleItemAllocation Item(decimal? manual = null, decimal weight = 0) =>
        new(Guid.NewGuid(), manual, weight);

    [Fact]
    public void Weighted_split_apportions_price_and_fees_and_sums_back_to_the_whole()
    {
        var a = Item(weight: 30);
        var b = Item(weight: 10);
        var lines = BundledSaleAllocator.Allocate([a, b], totalSoldPrice: 100m, platformFees: 10m, paymentFees: 0m, shippingCost: 4m, suppliesCost: 0m, otherCosts: 0m, salesTaxCollected: 0m);

        Assert.Equal(100m, lines.Sum(line => line.Price));
        Assert.Equal(10m, lines.Sum(line => line.PlatformFees));
        Assert.Equal(4m, lines.Sum(line => line.ShippingCost));
        // Higher-value item takes the larger share (75/25).
        Assert.Equal(75m, lines.First(line => line.InventoryItemId == a.InventoryItemId).Price);
        Assert.Equal(25m, lines.First(line => line.InventoryItemId == b.InventoryItemId).Price);
    }

    [Fact]
    public void Manual_prices_are_honored_and_the_rest_fills_the_remainder()
    {
        var manual = Item(manual: 60m);
        var rest = Item(weight: 1);
        var lines = BundledSaleAllocator.Allocate([manual, rest], totalSoldPrice: 100m, platformFees: 0m, paymentFees: 0m, shippingCost: 0m, suppliesCost: 0m, otherCosts: 0m, salesTaxCollected: 0m);

        Assert.Equal(60m, lines.First(line => line.InventoryItemId == manual.InventoryItemId).Price);
        Assert.Equal(40m, lines.First(line => line.InventoryItemId == rest.InventoryItemId).Price);
    }

    [Fact]
    public void Manual_prices_over_the_total_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BundledSaleAllocator.Allocate([Item(manual: 80m), Item(manual: 80m)], 100m, 0, 0, 0, 0, 0, 0));
    }
}
