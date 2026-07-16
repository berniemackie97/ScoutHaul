using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Services;

/// <summary>
/// Moves a sale through its workflow (recorded → packed → shipped → delivered, plus the exception states). Owns the
/// transition rules, the shipment record, inventory/listing side effects, the order's carrier + timestamps, and the
/// audited correction trail.
/// </summary>
public sealed class SaleTransitionWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderWriter,
    SaleFinancialStateService saleState)
{
    public async Task<AppResult<SaleDto>> TransitionAsync(Guid id, SaleStatusTransitionRequest request, CancellationToken cancellationToken)
    {
        Sale? sale = await db.Sales
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

        if (!SaleWorkflow.CanTransition(sale.Status, request.Status))
        {
            return AppResult<SaleDto>.Conflict($"A sale cannot move from {sale.Status} to {request.Status}.");
        }

        string? reason = TrimToNull(request.Reason);
        if (SaleWorkflow.RequiresReason(sale.Status, request.Status) && reason is null)
        {
            return AppResult<SaleDto>.Validation("A reason is required for this sale transition.");
        }

        string? carrier = TrimToNull(request.Carrier);
        string? trackingNumber = TrimToNull(request.TrackingNumber);
        if ((carrier is null) != (trackingNumber is null))
        {
            return AppResult<SaleDto>.Validation("Carrier and tracking number must be supplied together.");
        }

        DateTimeOffset occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
        if (occurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return AppResult<SaleDto>.Validation("Transition time cannot be in the future.");
        }

        if (occurredAt < sale.SoldAt)
        {
            return AppResult<SaleDto>.Validation("Transition time cannot be earlier than the sale date.");
        }

        Shipment? shipment = await db.Shipments.SingleOrDefaultAsync(
            item => item.SaleId == sale.Id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (request.Status == SaleStatus.Shipped && shipment?.PackedAt > occurredAt)
        {
            return AppResult<SaleDto>.Validation("Shipped time cannot be earlier than packed time.");
        }

        if (request.Status == SaleStatus.Delivered && shipment?.ShippedAt > occurredAt)
        {
            return AppResult<SaleDto>.Validation("Delivered time cannot be earlier than shipped time.");
        }

        SaleStatus previousStatus = sale.Status;
        string before = SaleFinancialStateService.Snapshot(sale);
        sale.Status = request.Status;
        if (sale.InventoryItem is not null)
        {
            if (request.Status != SaleStatus.ReturnRequested)
            {
                sale.InventoryItem.Status = SaleWorkflow.InventoryStatusFor(request.Status, sale.ListingId.HasValue);
            }

            if (request.Status == SaleStatus.Cancelled)
            {
                sale.InventoryItem.SoldAt = null;
            }

            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(
                sale.InventoryItem,
                request.Status == SaleStatus.Cancelled
                    ? InventoryStatus.Bundled
                    : request.Status == SaleStatus.ReturnRequested
                        ? sale.InventoryItem.Status
                        : SaleWorkflow.InventoryStatusFor(request.Status, sale.ListingId.HasValue),
                request.Status == SaleStatus.Cancelled ? null : sale.SoldAt);
        }

        if (sale.Listing is not null && request.Status == SaleStatus.Cancelled)
        {
            sale.Listing.Status = ListingStatus.Active;
            sale.Listing.EndedAt = null;
        }

        if (request.Status is SaleStatus.Packed or SaleStatus.Shipped or SaleStatus.Delivered)
        {
            shipment ??= new Shipment
            {
                OrganizationId = currentUser.OrganizationId,
                SaleId = sale.Id,
                Cost = sale.ShippingCost,
            };
            if (db.Entry(shipment).State == EntityState.Detached) db.Shipments.Add(shipment);
            if (request.Status == SaleStatus.Packed) shipment.PackedAt ??= occurredAt;
            if (request.Status == SaleStatus.Shipped)
            {
                shipment.PackedAt ??= occurredAt;
                shipment.ShippedAt ??= occurredAt;
                shipment.Carrier = carrier ?? shipment.Carrier;
                shipment.TrackingNumber = trackingNumber ?? shipment.TrackingNumber;
            }
            if (request.Status == SaleStatus.Delivered) shipment.DeliveredAt ??= occurredAt;
        }
        else if (previousStatus == SaleStatus.Packed && request.Status == SaleStatus.Recorded && shipment is not null)
        {
            shipment.PackedAt = null;
        }

        if (request.Status == SaleStatus.Cancelled)
        {
            MarketObservation? observation = await db.MarketObservations
                .Where(item => item.InventoryItemId == sale.InventoryItemId
                    && item.ObservationType == MarketObservationType.InternalSale
                    && item.OrganizationId == currentUser.OrganizationId
                    && item.SoldAt == sale.SoldAt)
                .OrderByDescending(item => item.ObservedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (observation is not null) db.MarketObservations.Remove(observation);
        }

        if (sale.Order is not null)
        {
            if (carrier is not null) sale.Order.ShippingCarrier = carrier;
            if (trackingNumber is not null) sale.Order.TrackingNumber = trackingNumber;
            if (request.Status == SaleStatus.Shipped) sale.Order.ShippedAt ??= occurredAt;
            if (request.Status == SaleStatus.Delivered) sale.Order.DeliveredAt ??= occurredAt;
            await orderWriter.SyncSalesOrderAsync(sale.Order, [sale], cancellationToken);
        }

        await saleState.AddCorrectionAsync(
            sale,
            before,
            reason ?? $"Workflow transition: {previousStatus} → {request.Status}",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SaleDto>.Ok(sale.ToDto());
    }
}
