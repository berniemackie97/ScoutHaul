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
/// Corrects a recorded sale's figures (price, fees, tax, date, channel) with a mandatory reason. Net profit is always
/// recomputed from the corrected inputs — a stored figure is never trusted — and the internal-sale comp plus the
/// order header are re-aligned so reports and Orders stay truthful.
/// </summary>
public sealed class SaleCorrectionWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderWriter,
    SalesChannelWorkflowService salesChannels,
    SaleFinancialStateService saleState)
{
    public async Task<AppResult<SaleDto>> UpdateAsync(Guid id, UpdateSaleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return AppResult<SaleDto>.Validation("A reason is required to correct a sale.");
        }

        string? channel = await salesChannels.CanonicalChannelAsync(request.Platform, cancellationToken);
        if (channel is null || request.SoldPrice <= 0)
        {
            return AppResult<SaleDto>.Validation("Select a controlled sales channel and enter a positive sold price.");
        }

        if (request.ShippingCharged < 0 || request.PlatformFees < 0 || request.PaymentFees < 0 || request.ShippingCost < 0
            || request.SuppliesCost < 0 || request.OtherCosts < 0 || request.SalesTaxCollected < 0)
        {
            return AppResult<SaleDto>.Validation("Revenue, costs, fees, and tax cannot be negative.");
        }

        if (request.SoldAt.UtcDateTime.Date > DateTime.UtcNow.Date)
        {
            return AppResult<SaleDto>.Validation("Sale date cannot be in the future.");
        }

        Sale? sale = await db.Sales
            .Include(item => item.Refunds)
            .Include(item => item.InventoryItem)
                .ThenInclude(item => item!.BundleComponents)
                    .ThenInclude(item => item.ComponentInventoryItem)
            .Include(item => item.Listing)
            .Include(item => item.Order)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (sale is null)
        {
            return AppResult<SaleDto>.NotFound();
        }

        string before = SaleFinancialStateService.Snapshot(sale);
        DateTimeOffset previousSoldAt = sale.SoldAt;

        sale.Platform = channel;
        sale.SoldPrice = request.SoldPrice;
        sale.ShippingCharged = request.ShippingCharged;
        sale.PlatformFees = request.PlatformFees;
        sale.PaymentFees = request.PaymentFees;
        sale.ShippingCost = request.ShippingCost;
        sale.SuppliesCost = request.SuppliesCost;
        sale.OtherCosts = request.OtherCosts;
        sale.SalesTaxCollected = request.SalesTaxCollected;
        sale.TaxRemittedByPlatform = request.TaxRemittedByPlatform;
        sale.SoldAt = request.SoldAt;
        sale.Notes = TrimToNull(request.Notes);
        if (sale.InventoryItem is not null) sale.InventoryItem.SoldAt = request.SoldAt;
        if (sale.InventoryItem is not null && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status))
        {
            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(
                sale.InventoryItem,
                SaleWorkflow.InventoryStatusFor(sale.Status, sale.ListingId.HasValue),
                request.SoldAt);
        }
        if (sale.Listing is not null && sale.Listing.Status == ListingStatus.Sold) sale.Listing.EndedAt = request.SoldAt;

        MarketObservation? observation = await db.MarketObservations
            .Where(item => item.InventoryItemId == sale.InventoryItemId
                && item.ObservationType == MarketObservationType.InternalSale
                && item.OrganizationId == currentUser.OrganizationId
                && item.SoldAt == previousSoldAt)
            .OrderByDescending(item => item.ObservedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (observation is not null)
        {
            observation.Price = request.SoldPrice;
            observation.TotalPrice = request.SoldPrice + request.ShippingCharged;
            observation.ObservedAt = request.SoldAt;
            observation.SoldAt = request.SoldAt;
        }

        // Net is always re-derived from the corrected inputs and this sale's refunds together (tax excluded);
        // never trust a stored figure, and never drop the refunds while correcting around them.
        sale.RecomputeNet();

        await saleState.AddCorrectionAsync(sale, before, request.Reason.Trim(), cancellationToken);
        if (sale.Order is not null)
        {
            await orderWriter.SyncSalesOrderAsync(sale.Order, [sale], cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SaleDto>.Ok(sale.ToDto());
    }
}
