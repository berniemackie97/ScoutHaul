using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// The listings whose photos are still owed to their items. The worker that drains this runs for the whole service
/// rather than inside one operator's request, so the question of which organizations have work is asked across all
/// of them; everything after that is asked inside one organization, through the ordinary filters.
/// </summary>
public sealed class EfInventoryPhotoSyncStore(HaulScoutDbContext db) : IInventoryPhotoSyncStore
{
    public async Task<IReadOnlyList<Guid>> ListOrganizationsWithPendingPhotosAsync(int take, DateTimeOffset retryBefore, CancellationToken cancellationToken) =>
        await Pending(db.PlatformListings.IgnoreQueryFilters(), retryBefore)
            .Select(listing => listing.OrganizationId)
            .Distinct()
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlatformListing>> ListPendingAsync(int take, DateTimeOffset retryBefore, CancellationToken cancellationToken) =>
        await Pending(db.PlatformListings, retryBefore)
            .Include(listing => listing.ReconciledInventoryItem!)
            .ThenInclude(item => item.Photos)
            .OrderBy(listing => listing.PhotoUrlsChangedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlatformListing>> ListPhotoSourcesForItemAsync(Guid inventoryItemId, CancellationToken cancellationToken) =>
        await db.PlatformListings
            .Where(listing => listing.ReconciledInventoryItemId == inventoryItemId && listing.PhotoUrlsKey != null)
            .ToListAsync(cancellationToken);

    public Task<int> CountPendingAsync(CancellationToken cancellationToken) =>
        Pending(db.PlatformListings, DateTimeOffset.MaxValue).CountAsync(cancellationToken);

    public void AddPhoto(InventoryPhoto photo) => db.InventoryPhotos.Add(photo);

    public void RemovePhoto(InventoryPhoto photo) => db.InventoryPhotos.Remove(photo);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// A listing owes its item photos when it has some, is linked to an item, its organization keeps photos in step
    /// with its marketplaces, and its photos have changed since they were last handed over. One that failed recently
    /// waits, so an unreachable image cannot be retried in a tight loop.
    /// </summary>
    private static IQueryable<PlatformListing> Pending(IQueryable<PlatformListing> listings, DateTimeOffset retryBefore) =>
        listings.Where(listing => listing.PhotoUrlsKey != null
            && listing.PhotoSyncedKey != listing.PhotoUrlsKey
            && listing.ReconciledInventoryItemId != null
            && listing.Organization!.MarketplacePhotoSyncEnabled
            && (listing.PhotoSyncError == null || listing.PhotosSyncedAt == null || listing.PhotosSyncedAt < retryBefore));
}
