using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Domain.Services;

namespace HaulScout.Api.Services;

/// <summary>
/// The one place a sale's money state is derived and audited. Refunds, returns, and corrections all mutate the same
/// invariants — net profit from the current refund set, the resulting sale/inventory status, and the immutable
/// before/after correction trail — so they share this rather than each re-deriving them.
/// </summary>
public sealed class SaleFinancialStateService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    /// <summary>Applies a sale's refund state and carries the result across to inventory, which is a separate
    /// aggregate and therefore this service's job rather than the sale's. The money and the sale's own status are
    /// derived by <see cref="Sale.ApplyRefunds"/>; only the inventory consequence is decided here.</summary>
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

    /// <summary>Serializes a sale's financial state for the immutable before/after audit snapshot.</summary>
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

    /// <summary>Appends a correction row (before → after) for the sale. Caller saves.</summary>
    public async Task AddCorrectionAsync(Sale sale, string before, string reason, CancellationToken cancellationToken)
    {
        int nextVersion = await db.SaleCorrections
            .CountAsync(item => item.SaleId == sale.Id && item.OrganizationId == currentUser.OrganizationId, cancellationToken) + 1;
        db.SaleCorrections.Add(new SaleCorrection
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            CorrectedByUserId = currentUser.UserId,
            Reason = reason,
            BeforeJson = before,
            AfterJson = Snapshot(sale),
            Version = nextVersion,
        });
    }
}
