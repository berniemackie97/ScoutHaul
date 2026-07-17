using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

public sealed class InventoryStatusWorkflowService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<AppResult<InventoryItemDto>> ChangeStatusAsync(
        Guid id,
        ChangeInventoryStatusRequest request,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await db.InventoryItems
            .Include(inventoryItem => inventoryItem.Photos)
            .Include(inventoryItem => inventoryItem.Issues)
            .Include(inventoryItem => inventoryItem.AcquisitionLotItems)
                .ThenInclude(lotItem => lotItem.AcquisitionLot)
            .Include(inventoryItem => inventoryItem.BundleComponents)
                .ThenInclude(component => component.ComponentInventoryItem)
            .SingleOrDefaultAsync(
                inventoryItem => inventoryItem.Id == id && inventoryItem.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryItemDto>.NotFound();
        }

        bool hasActiveSale = await db.Sales.AnyAsync(
            sale => sale.InventoryItemId == id
                && sale.OrganizationId == currentUser.OrganizationId
                && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status),
            cancellationToken);
        if (hasActiveSale)
        {
            return AppResult<InventoryItemDto>.Conflict("This item has an active sale. Change its stage through the sales workflow.");
        }

        if (request.Status is InventoryStatus.Bundled
            or InventoryStatus.Sold
            or InventoryStatus.Packed
            or InventoryStatus.Shipped
            or InventoryStatus.Delivered
            or InventoryStatus.Returned)
        {
            return AppResult<InventoryItemDto>.Conflict("Bundle, sale, and fulfillment stages must be changed through their dedicated workflows.");
        }

        item.Status = request.Status;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        item.PurchasedAt ??= request.Status == InventoryStatus.Purchased ? now : null;
        item.ListedAt ??= request.Status == InventoryStatus.Listed ? now : null;

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }
}
