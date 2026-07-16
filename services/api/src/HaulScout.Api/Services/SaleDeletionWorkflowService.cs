using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Permanently deletes a recorded sale. The item is released back to sellable inventory unless another live sale
/// still holds it, the internal-sale comp is removed, dependents are removed explicitly (not via DB cascade) so the
/// delete is deterministic, and an order left empty by the deletion is pruned rather than stranded as a phantom.
/// </summary>
public sealed class SaleDeletionWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderWriter)
{
    public async Task<AppResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
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
            return AppResult.NotFound();
        }

        if (sale.InventoryItem is not null)
        {
            bool otherActiveSale = await db.Sales.AnyAsync(
                other => other.InventoryItemId == sale.InventoryItemId && other.Id != id
                    && other.OrganizationId == currentUser.OrganizationId && !SaleWorkflow.InventoryReleaseStatuses.Contains(other.Status),
                cancellationToken);
            if (!otherActiveSale)
            {
                sale.InventoryItem.Status = sale.ListingId.HasValue ? InventoryStatus.Listed : InventoryStatus.ReadyToList;
                sale.InventoryItem.SoldAt = null;
                InventoryStatePropagation.ApplyBundleComponentInventoryStatus(sale.InventoryItem, sale.InventoryItem.Status, null);
            }
        }

        if (sale.Listing is not null && sale.Listing.Status == ListingStatus.Sold)
        {
            sale.Listing.Status = ListingStatus.Active;
            sale.Listing.EndedAt = null;
        }

        List<MarketObservation> observations = await db.MarketObservations
            .Where(observation => observation.InventoryItemId == sale.InventoryItemId
                && observation.ObservationType == MarketObservationType.InternalSale
                && observation.OrganizationId == currentUser.OrganizationId
                && observation.SoldAt == sale.SoldAt)
            .ToListAsync(cancellationToken);
        db.MarketObservations.RemoveRange(observations);

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = "Sale",
            EntityId = id,
            Action = "Deleted",
            DetailsJson = SaleFinancialStateService.Snapshot(sale),
        });

        // Remove the sale's dependents explicitly (rather than lean on DB cascade) so the delete is deterministic.
        db.Refunds.RemoveRange(await db.Refunds.Where(refund => refund.SaleId == id && refund.OrganizationId == currentUser.OrganizationId).ToListAsync(cancellationToken));
        db.SaleCorrections.RemoveRange(await db.SaleCorrections.Where(correction => correction.SaleId == id && correction.OrganizationId == currentUser.OrganizationId).ToListAsync(cancellationToken));
        Shipment? shipment = await db.Shipments.SingleOrDefaultAsync(item => item.SaleId == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (shipment is not null)
        {
            db.Shipments.Remove(shipment);
        }

        db.Sales.Remove(sale);
        Guid? affectedOrderId = sale.OrderId;
        if (sale.Order is not null)
        {
            await orderWriter.SyncSalesOrderAsync(sale.Order, [], cancellationToken, [sale.Id]);
        }
        await db.SaveChangesAsync(cancellationToken);

        // Deleting the last sale on a marketplace order would otherwise strand an empty phantom order (no lines,
        // $0, no buyer). Prune it once the sale removal is persisted so the emptiness check is accurate.
        if (affectedOrderId is Guid orderId)
        {
            await orderWriter.PruneEmptyMarketplaceOrderAsync(orderId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        return AppResult.Ok();
    }
}
