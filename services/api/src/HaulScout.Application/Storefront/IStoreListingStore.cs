using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>The admin shelf-management port: listings, categories, and the reconcile sweep.</summary>
public interface IStoreListingStore
{
    Task<IReadOnlyList<StoreListing>> ListListingsAsync(Guid organizationId, StoreListingStatus? status, CancellationToken cancellationToken);

    Task<StoreListing?> FindListingAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken);

    /// <summary>The listing with its backing item loaded — unpublish moves both together.</summary>
    Task<StoreListing?> FindListingWithItemAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken);

    Task<StoreListing?> FindListingByItemAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken);

    /// <summary>The item as the publish gate needs it: photos loaded.</summary>
    Task<InventoryItem?> FindItemForPublishingAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoreCategory>> ListCategoriesAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<bool> CategoryExistsAsync(Guid organizationId, Guid categoryId, CancellationToken cancellationToken);

    Task<string> EnsureUniqueCategorySlugAsync(Guid organizationId, string baseSlug, CancellationToken cancellationToken);

    Task AddCategoryAsync(StoreCategory category, CancellationToken cancellationToken);

    /// <summary>
    /// Sweeps the whole catalog with the same shelf rule the save-time synchronizer applies to changes — the
    /// backfill for rows that predate it and the repair for anything written around EF. The rule is the domain's
    /// (StorePublishing); running it over everything is pure mechanism, so the sweep lives with the mechanism.
    /// </summary>
    Task<ShelfReconciliation> ReconcileShelfAsync(Guid organizationId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>What one reconcile sweep found and did.</summary>
public sealed record ShelfReconciliation(
    int ItemsExamined,
    int ListingsCreated,
    int NowActive,
    int AwaitingPhotos,
    int Retired);
