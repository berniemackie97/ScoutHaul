using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

public interface IInventoryPhotoSyncStore
{
    /// <summary>
    /// Organizations with listing photos waiting to reach their items. Read across every organization, because the
    /// worker that drains this runs for the whole service rather than inside one operator's request.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListOrganizationsWithPendingPhotosAsync(int take, DateTimeOffset retryBefore, CancellationToken cancellationToken);

    /// <summary>
    /// Listings whose photos are not yet on the item they are linked to, for accounts whose photo sync is on,
    /// oldest change first — with the item and its photos loaded, since the whole set is rearranged at once.
    /// A listing that failed recently waits until <paramref name="retryBefore"/> to be tried again.
    /// </summary>
    Task<IReadOnlyList<PlatformListing>> ListPendingAsync(int take, DateTimeOffset retryBefore, CancellationToken cancellationToken);

    /// <summary>Every listing that could give this item its photos, so the richest one is the one that does.</summary>
    Task<IReadOnlyList<PlatformListing>> ListPhotoSourcesForItemAsync(Guid inventoryItemId, CancellationToken cancellationToken);

    /// <summary>How many linked listings are still waiting to hand their photos over, by account.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPendingByAccountAsync(CancellationToken cancellationToken);

    void AddPhoto(InventoryPhoto photo);

    void RemovePhoto(InventoryPhoto photo);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
