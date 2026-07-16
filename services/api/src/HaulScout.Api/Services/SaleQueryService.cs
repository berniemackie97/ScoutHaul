using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Domain.Services;

namespace HaulScout.Api.Services;

/// <summary>
/// Every read of a sale: the plain list, the searchable/paged finance table with its P&amp;L summary, a single sale,
/// its realized profit, shipment, corrections, and refunds. Reads stay separate from the write workflows so the
/// controller has no reason to hold a DbContext.
/// </summary>
public sealed class SaleQueryService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    private IQueryable<Sale> Scoped() => db.Sales
        .Include(item => item.InventoryItem)
        .Include(item => item.Order)
        .Include(item => item.Refunds)
        .Where(item => item.OrganizationId == currentUser.OrganizationId);

    public async Task<IReadOnlyList<SaleDto>> ListAsync(SaleStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<Sale> query = Scoped();
        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        List<Sale> sales = await query
            .OrderByDescending(item => item.SoldAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return sales.Select(item => item.ToDto()).ToList();
    }

    /// <summary>Searchable, sortable, paged sales for the finance table, plus the P&amp;L summary over the whole
    /// filtered set (so the KPIs stay correct while the table pages).</summary>
    public async Task<SaleListDto> BrowseAsync(
        SaleStatus? status,
        string? search,
        string? sort,
        bool desc,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<Sale> query = Scoped();
        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        string? term = TrimToNull(search)?.ToLower();
        if (term is not null)
        {
            query = query.Where(item =>
                (item.InventoryItem != null && item.InventoryItem.Title.ToLower().Contains(term))
                || item.Platform.ToLower().Contains(term)
                || (item.Order != null && item.Order.ExternalOrderId != null && item.Order.ExternalOrderId.ToLower().Contains(term))
                || (item.Order != null && item.Order.TrackingNumber != null && item.Order.TrackingNumber.ToLower().Contains(term))
                || (item.GroupOrderReference != null && item.GroupOrderReference.ToLower().Contains(term)));
        }

        List<Sale> filtered = await query.ToListAsync(cancellationToken);
        SalesSummaryDto summary = Summarize(filtered);

        IEnumerable<Sale> ordered = sort?.ToLowerInvariant() switch
        {
            "netprofit" => desc ? filtered.OrderByDescending(item => item.NetProfit) : filtered.OrderBy(item => item.NetProfit),
            "soldprice" => desc ? filtered.OrderByDescending(item => item.SoldPrice) : filtered.OrderBy(item => item.SoldPrice),
            "item" => desc ? filtered.OrderByDescending(item => item.InventoryItem!.Title) : filtered.OrderBy(item => item.InventoryItem!.Title),
            _ => desc ? filtered.OrderByDescending(item => item.SoldAt) : filtered.OrderBy(item => item.SoldAt),
        };

        List<SaleDto> items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(item => item.ToDto()).ToList();
        int pageCount = (int)Math.Ceiling(filtered.Count / (double)pageSize);
        return new SaleListDto(items, filtered.Count, page, pageSize, Math.Max(1, pageCount), summary);
    }

    /// <summary>The P&amp;L over a filtered set. Only realized sales count — a cancelled row is not revenue.</summary>
    private static SalesSummaryDto Summarize(IReadOnlyCollection<Sale> filtered)
    {
        List<Sale> realized = filtered.Where(item => SaleWorkflow.IsRealized(item.Status)).ToList();
        decimal itemSales = realized.Sum(item => item.SoldPrice);
        decimal buyerShipping = realized.Sum(item => item.ShippingCharged);
        decimal taxCollected = realized.Sum(item => item.SalesTaxCollected);
        decimal platformFees = realized.Sum(item => item.PlatformFees);
        decimal paymentFees = realized.Sum(item => item.PaymentFees);
        decimal shippingLabels = realized.Sum(item => item.ShippingCost);
        decimal suppliesAndOther = realized.Sum(item => item.SuppliesCost + item.OtherCosts);
        decimal sellingCosts = platformFees + paymentFees + shippingLabels + suppliesAndOther;
        return new(
            realized.Count,
            itemSales,
            buyerShipping,
            taxCollected,
            itemSales + buyerShipping + taxCollected,
            platformFees,
            paymentFees,
            shippingLabels,
            suppliesAndOther,
            sellingCosts,
            itemSales + buyerShipping - sellingCosts,
            realized.Sum(item => item.CostOfGoods),
            realized.Sum(item => item.NetProfit),
            realized.Sum(item => ResaleFinancialCalculator.SalesTaxOwed(item.SalesTaxCollected, item.TaxRemittedByPlatform)),
            realized.Sum(item => item.Refunds.Sum(refund => refund.ItemAmount + refund.ShippingRefunded)));
    }

    public async Task<AppResult<SaleDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Sale? sale = await Scoped().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return sale is null ? AppResult<SaleDto>.NotFound() : AppResult<SaleDto>.Ok(sale.ToDto());
    }

    public async Task<AppResult<RealizedProfitDto>> GetProfitAsync(Guid id, CancellationToken cancellationToken)
    {
        Sale? sale = await db.Sales
            .Include(item => item.InventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (sale is null)
        {
            return AppResult<RealizedProfitDto>.NotFound();
        }

        RealizedSaleFinancials financials = ResaleFinancialCalculator.CalculateRealizedSale(
            sale.SoldPrice,
            sale.ShippingCharged,
            sale.CostOfGoods,
            sale.PlatformFees,
            sale.PaymentFees,
            sale.ShippingCost,
            sale.SuppliesCost,
            sale.OtherCosts);

        return AppResult<RealizedProfitDto>.Ok(new RealizedProfitDto(
            sale.Id,
            financials.SoldPrice,
            financials.ShippingCharged,
            financials.SalesRevenue,
            financials.PurchaseCost,
            financials.SellingCosts,
            financials.TotalFeesAndCosts,
            financials.MarketplaceNetBeforeCogs,
            ResaleFinancialCalculator.SalesTaxOwed(sale.SalesTaxCollected, sale.TaxRemittedByPlatform),
            financials.NetProfit,
            financials.RoiPercent,
            financials.MarginPercent));
    }

    public async Task<AppResult<SaleShipmentDto>> GetShipmentAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await SaleExistsAsync(id, cancellationToken))
        {
            return AppResult<SaleShipmentDto>.NotFound();
        }

        Shipment? shipment = await db.Shipments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        return shipment is null ? AppResult<SaleShipmentDto>.NotFound() : AppResult<SaleShipmentDto>.Ok(shipment.ToDto());
    }

    public async Task<AppResult<IReadOnlyList<SaleCorrectionDto>>> ListCorrectionsAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await SaleExistsAsync(id, cancellationToken))
        {
            return AppResult<IReadOnlyList<SaleCorrectionDto>>.NotFound();
        }

        List<SaleCorrection> corrections = await db.SaleCorrections
            .Include(item => item.CorrectedByUser)
            .Where(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.Version)
            .ToListAsync(cancellationToken);
        return AppResult<IReadOnlyList<SaleCorrectionDto>>.Ok(corrections.Select(item => item.ToDto()).ToList());
    }

    public async Task<AppResult<IReadOnlyList<RefundDto>>> ListRefundsAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await SaleExistsAsync(id, cancellationToken))
        {
            return AppResult<IReadOnlyList<RefundDto>>.NotFound();
        }

        List<Refund> refunds = await db.Refunds
            .Include(item => item.RecordedByUser)
            .Where(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.RefundedAt)
            .ToListAsync(cancellationToken);
        return AppResult<IReadOnlyList<RefundDto>>.Ok(refunds.Select(item => item.ToDto()).ToList());
    }

    private Task<bool> SaleExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sales.AnyAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
}
