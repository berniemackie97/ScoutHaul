using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>What could stop an item being folded into another, read before anything moves.</summary>
public sealed record MergeFootprint(
    int LiveSales,
    bool InPurchaseLot,
    bool InBundle,
    StoreListingStatus? StoreListing,
    bool StoreListingHasHistory);

/// <summary>Persistence for merging duplicate inventory records. Every row that points at an item is the store's to know.</summary>
public interface IInventoryMergeStore
{
    /// <summary>
    /// The items, tracked, with their photos — and locked until the caller's transaction ends, so a sync or import
    /// writing to one of them waits for the merge instead of attaching something to a record about to be deleted.
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> LockItemsAsync(Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<MergeFootprint> InspectAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Points every row that refers to <paramref name="from"/>, other than its photos, at <paramref name="into"/>.
    /// Where only one of something may exist per item — a listing on a channel account, a storefront record — the
    /// kept item's stays, unless the duplicate's channel listing says more.
    /// </summary>
    Task MoveReferencesAsync(InventoryItem from, InventoryItem into, CancellationToken cancellationToken);

    /// <summary>Deletes a duplicate's photo the kept item already has, pinning any issue on it to the kept copy.</summary>
    Task DropPhotoAsync(InventoryPhoto photo, InventoryPhoto keptCopy, CancellationToken cancellationToken);

    void Remove(InventoryItem item);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
