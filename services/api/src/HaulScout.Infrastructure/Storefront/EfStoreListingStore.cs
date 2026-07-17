using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

public sealed class EfStoreListingStore(HaulScoutDbContext db, TimeProvider timeProvider) : IStoreListingStore
{
    public async Task<IReadOnlyList<StoreListing>> ListListingsAsync(
        Guid organizationId,
        StoreListingStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<StoreListing> query = db.StoreListings.Where(listing => listing.OrganizationId == organizationId);
        if (status is not null)
        {
            query = query.Where(listing => listing.Status == status);
        }

        return await query.OrderByDescending(listing => listing.CreatedAt).ToListAsync(cancellationToken);
    }

    public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken) =>
        db.StoreListings.FirstOrDefaultAsync(
            listing => listing.Id == listingId && listing.OrganizationId == organizationId, cancellationToken);

    public Task<StoreListing?> FindListingWithItemAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken) =>
        db.StoreListings
            .Include(listing => listing.InventoryItem)
            .FirstOrDefaultAsync(listing => listing.Id == listingId && listing.OrganizationId == organizationId, cancellationToken);

    public Task<StoreListing?> FindListingByItemAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken) =>
        db.StoreListings.FirstOrDefaultAsync(
            listing => listing.InventoryItemId == inventoryItemId && listing.OrganizationId == organizationId, cancellationToken);

    public Task<InventoryItem?> FindItemForPublishingAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken) =>
        db.InventoryItems
            .Include(item => item.Photos)
            .FirstOrDefaultAsync(item => item.Id == inventoryItemId && item.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<StoreCategory>> ListCategoriesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.StoreCategories
            .Where(category => category.OrganizationId == organizationId)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> CategoryExistsAsync(Guid organizationId, Guid categoryId, CancellationToken cancellationToken) =>
        db.StoreCategories.AnyAsync(
            category => category.Id == categoryId && category.OrganizationId == organizationId, cancellationToken);

    public async Task<string> EnsureUniqueCategorySlugAsync(Guid organizationId, string baseSlug, CancellationToken cancellationToken)
    {
        string stem = string.IsNullOrEmpty(baseSlug) ? "category" : baseSlug;
        string candidate = stem;
        int suffix = 2;
        while (await db.StoreCategories.AnyAsync(
            category => category.OrganizationId == organizationId && category.Slug == candidate, cancellationToken))
        {
            candidate = $"{stem}-{suffix++}";
        }

        return candidate;
    }

    public async Task AddCategoryAsync(StoreCategory category, CancellationToken cancellationToken) =>
        await db.StoreCategories.AddAsync(category, cancellationToken);

    public async Task<ShelfReconciliation> ReconcileShelfAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // Everything the rule could act on: shelf candidates plus anything already carrying a listing.
        List<Guid> itemIds = await db.InventoryItems
            .Where(item => item.OrganizationId == organizationId
                && (item.Status == InventoryStatus.Listed
                    || db.StoreListings.Any(listing => listing.InventoryItemId == item.Id)))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, StoreListingStatus> before = await db.StoreListings
            .Where(listing => listing.OrganizationId == organizationId)
            .ToDictionaryAsync(listing => listing.InventoryItemId, listing => listing.Status, cancellationToken);

        await StoreListingSynchronizer.ApplyAsync(db, itemIds, timeProvider.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        List<StoreListing> after = await db.StoreListings
            .Include(listing => listing.InventoryItem)
            .Where(listing => listing.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        return new ShelfReconciliation(
            ItemsExamined: itemIds.Count,
            ListingsCreated: after.Count(listing => !before.ContainsKey(listing.InventoryItemId)),
            NowActive: after.Count(listing => listing.Status == StoreListingStatus.Active),
            AwaitingPhotos: after.Count(listing => listing.Status == StoreListingStatus.Draft
                && listing.InventoryItem?.Status == InventoryStatus.Listed),
            Retired: after.Count(listing =>
                before.TryGetValue(listing.InventoryItemId, out StoreListingStatus was)
                && was == StoreListingStatus.Active
                && listing.Status is StoreListingStatus.Draft or StoreListingStatus.Archived or StoreListingStatus.Sold));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
