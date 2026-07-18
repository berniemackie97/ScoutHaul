using System.Text.Json;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sales;

/// <summary>
/// Pure sale financial-state derivation shared by sales workflows and marketplace imports. Persistence/auditing stays
/// in the caller's store; this type owns only the deterministic "facts in, sale state out" rule.
/// </summary>
public static class SaleFinancialState
{
    /// <summary>Applies the sale's current refund set to net/status and propagates the inventory consequence.</summary>
    public static void ApplyRefundState(Sale sale)
    {
        bool restocked = sale.ApplyRefunds();

        if (sale.InventoryItem is null)
        {
            return;
        }

        if (sale.Refunds.Count == 0)
        {
            if (sale.InventoryItem.Status == InventoryStatus.Returned)
            {
                sale.InventoryItem.Status = InventoryStatus.Delivered;
                InventoryStatePropagation.ApplyBundleComponentInventoryStatus(sale.InventoryItem, InventoryStatus.Delivered, sale.InventoryItem.SoldAt);
            }
            return;
        }

        if (restocked)
        {
            sale.InventoryItem.Status = InventoryStatus.Returned;
            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(sale.InventoryItem, InventoryStatus.Returned, sale.InventoryItem.SoldAt);
        }
        // Partial or full-but-kept: the buyer still has the item, so inventory state is unchanged.
    }

    /// <summary>Serializes a sale's financial state for immutable before/after audit snapshots.</summary>
    public static string Snapshot(Sale sale) => JsonSerializer.Serialize(new
    {
        sale.Platform,
        sale.SoldPrice,
        sale.ShippingCharged,
        sale.CostOfGoods,
        sale.PlatformFees,
        sale.PaymentFees,
        sale.ShippingCost,
        sale.SuppliesCost,
        sale.OtherCosts,
        sale.SalesTaxCollected,
        sale.TaxRemittedByPlatform,
        sale.NetProfit,
        sale.SoldAt,
        Status = sale.Status.ToString(),
        sale.Notes,
    });
}
