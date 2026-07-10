namespace HaulScout.Domain;

/// <summary>One item's share of a bundled (multi-item) order's proceeds and costs.</summary>
public sealed record AllocatedSaleLine(
    Guid InventoryItemId,
    decimal Price,
    decimal ShippingCharged,
    decimal PlatformFees,
    decimal PaymentFees,
    decimal ShippingCost,
    decimal SuppliesCost,
    decimal OtherCosts,
    decimal SalesTaxCollected);

public sealed record BundledSaleItemAllocation(Guid InventoryItemId, decimal? ManualPrice, decimal Weight);

/// <summary>
/// Splits one bundled order's total price and shared costs across its items so each item carries its own
/// price, fees, and tax (and therefore its own true net). Prices use manual amounts where given and weight the
/// rest by expected value; every shared cost is then apportioned in proportion to each item's final price.
/// Rounding remainder lands on the last line so the parts always sum back to the whole.
/// </summary>
public static class BundledSaleAllocator
{
    public static IReadOnlyList<AllocatedSaleLine> Allocate(
        IReadOnlyList<BundledSaleItemAllocation> items,
        decimal totalSoldPrice,
        decimal shippingCharged,
        decimal platformFees,
        decimal paymentFees,
        decimal shippingCost,
        decimal suppliesCost,
        decimal otherCosts,
        decimal salesTaxCollected)
    {
        if (items.Count == 0) throw new InvalidOperationException("A bundled sale needs at least one item.");

        decimal total = Round(totalSoldPrice);
        decimal manualSum = Round(items.Where(item => item.ManualPrice.HasValue).Sum(item => item.ManualPrice!.Value));
        if (manualSum > total + 0.01m) throw new InvalidOperationException("Per-item prices exceed the order total.");

        List<BundledSaleItemAllocation> withoutManual = items.Where(item => !item.ManualPrice.HasValue).ToList();
        decimal remaining = total - manualSum;
        decimal weightSum = withoutManual.Sum(item => item.Weight);
        if (withoutManual.Count > 0 && weightSum <= 0)
        {
            weightSum = withoutManual.Count;
        }

        // Resolve each item's price.
        Dictionary<Guid, decimal> prices = new();
        decimal spread = 0;
        for (int index = 0; index < withoutManual.Count; index++)
        {
            BundledSaleItemAllocation item = withoutManual[index];
            decimal weight = weightSum == withoutManual.Count ? 1 : item.Weight;
            decimal price = index == withoutManual.Count - 1 ? remaining - spread : Round(remaining * weight / weightSum);
            prices[item.InventoryItemId] = price;
            spread += price;
        }
        foreach (BundledSaleItemAllocation item in items.Where(item => item.ManualPrice.HasValue))
        {
            prices[item.InventoryItemId] = Round(item.ManualPrice!.Value);
        }

        // Apportion every shared cost in proportion to price; last line absorbs rounding.
        List<AllocatedSaleLine> lines = [];
        decimal[] running = new decimal[7];
        for (int index = 0; index < items.Count; index++)
        {
            Guid id = items[index].InventoryItemId;
            decimal price = prices[id];
            bool last = index == items.Count - 1;
            decimal Share(decimal amount, int slot)
            {
                decimal value = last ? Round(amount) - running[slot] : (total <= 0 ? 0 : Round(amount * price / total));
                running[slot] += value;
                return value;
            }
            lines.Add(new AllocatedSaleLine(
                id,
                price,
                Share(shippingCharged, 0),
                Share(platformFees, 1),
                Share(paymentFees, 2),
                Share(shippingCost, 3),
                Share(suppliesCost, 4),
                Share(otherCosts, 5),
                Share(salesTaxCollected, 6)));
        }

        return lines;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
