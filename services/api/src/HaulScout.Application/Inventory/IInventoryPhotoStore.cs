using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

public interface IInventoryPhotoStore
{
    /// <summary>The item with its photos loaded — every photo use case works on the whole set, because hero and
    /// ordering are properties of the set rather than of one row.</summary>
    Task<InventoryItem?> FindItemWithPhotosAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task<InventoryPhoto?> FindPhotoForOrganizationAsync(Guid organizationId, Guid inventoryItemId, Guid photoId, CancellationToken cancellationToken);

    Task AddAsync(InventoryPhoto photo, CancellationToken cancellationToken);

    void Remove(InventoryPhoto photo);

    /// <summary>Reloads an item's photos after a write, so the caller can return the resulting set.</summary>
    Task LoadPhotosAsync(InventoryItem item, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
