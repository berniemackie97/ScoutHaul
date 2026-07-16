using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// The one place a sale's money state is derived and audited. Refunds, returns, and corrections all mutate the same
/// invariants — net profit from the current refund set, the resulting sale/inventory status, and the immutable
/// before/after correction trail — so they share this rather than each re-deriving them.
/// </summary>
public sealed class SaleFinancialStateService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    /// <summary>Recomputes only a sale's net from its refunds + current cost fields, leaving status untouched (used
    /// when a cost like return shipping changes but no refund/inventory transition should be implied).</summary>
    public static void RecomputeNet(Sale sale)
    {
        sale.NetProfit = NetFrom(sale, RefundImpact(sale));
    }

    /// <summary>Recomputes a sale's net profit and status from its current set of refunds. Item + buyer shipping
    /// reduce revenue, the platform's fee credit reduces selling cost, a returned item drops COGS; tax never nets.
    /// Full/partial + restock decide the resulting status and whether the item releases back to inventory.</summary>
    public static void ApplyRefundState(Sale sale)
    {
        ICollection<Refund> refunds = sale.Refunds;
        decimal itemRefunded = refunds.Sum(refund => refund.ItemAmount);
        bool restocked = refunds.Any(refund => refund.ItemReturned);
        sale.NetProfit = NetFrom(sale, RefundImpact(sale));

        if (refunds.Count == 0)
        {
            // Last refund voided — revert to a realized state; the operator can re-set the exact status.
            sale.Status = SaleStatus.Delivered;
            if (sale.InventoryItem is not null && sale.InventoryItem.Status == InventoryStatus.Returned)
            {
                sale.InventoryItem.Status = InventoryStatus.Delivered;
                InventoryStatePropagation.ApplyBundleComponentInventoryStatus(sale.InventoryItem, InventoryStatus.Delivered, sale.InventoryItem.SoldAt);
            }
            return;
        }

        bool isFull = refunds.Any(refund => refund.Type == RefundType.Full) || (sale.SoldPrice > 0 && itemRefunded >= sale.SoldPrice - 0.01m);
        sale.Status = restocked ? SaleStatus.Returned : isFull ? SaleStatus.Refunded : SaleStatus.PartiallyRefunded;

        if (sale.InventoryItem is not null && restocked)
        {
            sale.InventoryItem.Status = InventoryStatus.Returned;
            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(sale.InventoryItem, InventoryStatus.Returned, sale.InventoryItem.SoldAt);
        }
        // Partial or full-but-kept: the buyer still has the item, so inventory state is unchanged.
    }

    private static ResaleFinancialCalculator.SaleRefundImpact RefundImpact(Sale sale)
    {
        ICollection<Refund> refunds = sale.Refunds;
        return new(
            refunds.Sum(refund => refund.ItemAmount),
            refunds.Sum(refund => refund.ShippingRefunded),
            refunds.Sum(refund => refund.TaxRefunded),
            refunds.Sum(refund => refund.FeeCredit),
            refunds.Any(refund => refund.ItemReturned));
    }

    private static decimal NetFrom(Sale sale, ResaleFinancialCalculator.SaleRefundImpact impact) =>
        ResaleFinancialCalculator.NetProfitAfterRefunds(
            sale.SoldPrice, sale.ShippingCharged, sale.CostOfGoods, sale.PlatformFees, sale.PaymentFees,
            sale.ShippingCost, sale.SuppliesCost, sale.OtherCosts, impact);

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
