using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Defects recorded against an item — what is wrong with it, optionally pinned to the photo that shows it. Separate
/// from <see cref="IInventoryStore"/> because issues are their own list with their own ordering, and the use case that
/// edits them never needs an item's photos, lots or bundle components loaded.
/// </summary>
public interface IInventoryIssueStore
{
    Task<IReadOnlyList<InventoryIssue>> ListAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken);

    Task<InventoryIssue?> FindAsync(Guid organizationId, Guid inventoryItemId, Guid issueId, CancellationToken cancellationToken);

    /// <summary>Whether a photo belongs to this item — an issue may only point at its own item's photo.</summary>
    Task<bool> PhotoBelongsToItemAsync(Guid organizationId, Guid inventoryItemId, Guid photoId, CancellationToken cancellationToken);

    /// <summary>The next free position in the item's issue list, for callers that did not choose one.</summary>
    Task<int> NextSortOrderAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken);

    Task AddAsync(InventoryIssue issue, CancellationToken cancellationToken);

    void Remove(InventoryIssue issue);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
