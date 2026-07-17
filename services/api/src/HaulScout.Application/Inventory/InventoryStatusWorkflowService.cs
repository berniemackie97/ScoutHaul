using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Moving an item by hand between its own stages. The stages an item reaches as a consequence of something else —
/// bundling, selling, fulfilment — are deliberately refused here: those are owned by the workflows that also do the
/// rest of the work, and letting this back door set them would leave the sale or bundle inconsistent with the item.
/// </summary>
public sealed class InventoryStatusWorkflowService(
    IInventoryStore inventory,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    /// <summary>Stages that are a side effect of another workflow, and so cannot be set directly.</summary>
    private static readonly InventoryStatus[] WorkflowOwnedStatuses =
    [
        InventoryStatus.Bundled,
        InventoryStatus.Sold,
        InventoryStatus.Packed,
        InventoryStatus.Shipped,
        InventoryStatus.Delivered,
        InventoryStatus.Returned,
    ];

    public async Task<AppResult<InventoryItemDto>> ChangeStatusAsync(
        Guid id,
        ChangeInventoryStatusRequest request,
        CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;

        InventoryItem? item = await inventory.FindDetailAsync(organizationId, id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryItemDto>.NotFound();
        }

        if (await inventory.HasActiveSaleAsync(organizationId, id, cancellationToken))
        {
            return AppResult<InventoryItemDto>.Conflict("This item has an active sale. Change its stage through the sales workflow.");
        }

        if (WorkflowOwnedStatuses.Contains(request.Status))
        {
            return AppResult<InventoryItemDto>.Conflict("Bundle, sale, and fulfillment stages must be changed through their dedicated workflows.");
        }

        item.Status = request.Status;
        DateTimeOffset now = timeProvider.GetUtcNow();
        item.PurchasedAt ??= request.Status == InventoryStatus.Purchased ? now : null;
        item.ListedAt ??= request.Status == InventoryStatus.Listed ? now : null;

        await inventory.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryItemDto>.Ok(item.ToDto());
    }
}
