using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>Reads an item with the marketplace listings linked to it, to compare and correct their details.</summary>
public interface IInventoryListingDifferenceStore
{
    /// <summary>The item, tracked, or null when the organization has no such item.</summary>
    Task<InventoryItem?> FindItemAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>The listings linked to the item, read-only, newest first.</summary>
    Task<IReadOnlyList<PlatformListing>> ListLinkedListingsAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
