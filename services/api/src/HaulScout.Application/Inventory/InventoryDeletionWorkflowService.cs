using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Removing an item, in the two senses the system means it: archiving (hide it, keep the row) and hard deletion
/// (destroy it). The rules for both live in <see cref="InventoryDeletionRules"/>; this coordinates them with the
/// writes.
/// </summary>
public sealed class InventoryDeletionWorkflowService(
    IInventoryStore inventory,
    IAuditTrail audit,
    ICurrentUserContext currentUser)
{
    public async Task<AppResult> DeleteAsync(Guid id, bool hard, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;

        InventoryItem? item = await inventory.FindWithBundleComponentsAsync(organizationId, id, cancellationToken);
        if (item is null)
        {
            return AppResult.NotFound();
        }

        InventoryReferences references = await inventory.InspectReferencesAsync(organizationId, id, cancellationToken);

        if (InventoryDeletionRules.BlockedFromRemoval(item, references) is string blocked)
        {
            return AppResult.Conflict(blocked);
        }

        if (hard)
        {
            if (InventoryDeletionRules.BlockedFromHardDelete(references) is string blockedFromDelete)
            {
                return AppResult.Conflict(blockedFromDelete);
            }

            InventoryDeletionRules.ReleaseBundleComponents(item);
            await inventory.RemoveWithDependentsAsync(item, cancellationToken);
            await audit.RecordAsync(nameof(InventoryItem), item.Id, "InventoryDeleted", Details(item), cancellationToken);
            await inventory.SaveChangesAsync(cancellationToken);
            return AppResult.Ok();
        }

        item.IsArchived = true;
        InventoryDeletionRules.ReleaseBundleComponents(item);
        await audit.RecordAsync(nameof(InventoryItem), item.Id, "InventoryArchived", Details(item), cancellationToken);

        await inventory.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private static object Details(InventoryItem item) => new { item.Title, item.Status, item.IsBundle };
}
