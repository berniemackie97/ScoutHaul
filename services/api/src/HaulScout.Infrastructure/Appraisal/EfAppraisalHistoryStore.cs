using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Appraisal;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Appraisal;

/// <summary>
/// Appraisal snapshots live in MarketAnalyses, the table the retired deal analyzer wrote to. Its basis
/// column predates PriceBasis, so the mapping below is the only place the two vocabularies meet.
/// </summary>
internal sealed class EfAppraisalHistoryStore(HaulScoutDbContext db) : IAppraisalHistoryStore
{
    public Task<bool> ItemExistsAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken) =>
        db.InventoryItems.AnyAsync(item => item.OrganizationId == organizationId && item.Id == inventoryItemId, cancellationToken);

    public async Task RecordAsync(
        Guid organizationId,
        Guid inventoryItemId,
        Guid? productIdentityId,
        PriceResult price,
        CancellationToken cancellationToken)
    {
        List<Comp> used = price.Comps.Where(comp => comp.Included).Select(comp => comp.Comp).ToList();
        int sales = used.Count(comp => comp.Kind is CompKind.Sold or CompKind.YourSale);
        bool onlyOwnSales = sales > 0 && used.All(comp => comp.Kind != CompKind.Sold);

        db.MarketAnalyses.Add(new MarketAnalysis
        {
            OrganizationId = organizationId,
            InventoryItemId = inventoryItemId,
            ProductIdentityId = productIdentityId,
            EstimatedSalePrice = price.List,
            PricingBasis = price.Basis switch
            {
                PriceBasis.Sales => onlyOwnSales ? PricingBasisType.InternalHistory : PricingBasisType.LiveValidated,
                PriceBasis.Guide => PricingBasisType.CachedRecent,
                PriceBasis.None => PricingBasisType.Unavailable,
                _ => PricingBasisType.ManualEstimate,
            },
            ConfidenceScore = price.Confidence,
            SoldCompCount = sales,
            ActiveListingCount = used.Count(comp => comp.Kind == CompKind.Asking),
            ReasonsJson = JsonSerializer.Serialize(price.Reasons),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppraisalSnapshotDto>> ListForItemAsync(
        Guid organizationId,
        Guid inventoryItemId,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await db.MarketAnalyses
            .AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == inventoryItemId)
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(take)
            .Select(entry => new
            {
                entry.Id,
                entry.CreatedAt,
                entry.EstimatedSalePrice,
                entry.PricingBasis,
                entry.ConfidenceScore,
                entry.SoldCompCount,
                entry.ActiveListingCount,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            PriceBasis basis = row.PricingBasis switch
            {
                PricingBasisType.LiveValidated or PricingBasisType.InternalHistory => PriceBasis.Sales,
                PricingBasisType.CachedRecent => PriceBasis.Guide,
                PricingBasisType.Unavailable => PriceBasis.None,
                _ => row.ActiveListingCount > 0 && row.SoldCompCount == 0 ? PriceBasis.Asking : PriceBasis.Estimate,
            };
            return new AppraisalSnapshotDto(
                row.Id,
                row.CreatedAt,
                row.EstimatedSalePrice,
                basis,
                row.ConfidenceScore,
                CompPricing.LevelFor(row.ConfidenceScore, basis),
                row.SoldCompCount,
                row.ActiveListingCount);
        }).ToArray();
    }
}
