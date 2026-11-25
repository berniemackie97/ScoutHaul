using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

public sealed class EfListingMatchStore(HaulScoutDbContext db) : IListingMatchStore
{
    public Task<PlatformListing?> FindListingAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken) =>
        db.PlatformListings.AsNoTracking().FirstOrDefaultAsync(listing => listing.OrganizationId == organizationId && listing.Id == listingId, cancellationToken);

    public async Task<IReadOnlyList<MatchPoolItem>> ListPoolAsync(
        Guid organizationId,
        string? search,
        IReadOnlyCollection<Guid> alwaysInclude,
        int take,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryItem> items = db.InventoryItems.AsNoTracking().Where(item => item.OrganizationId == organizationId && !item.IsArchived);
        if (search is not null)
        {
            // Lowered and contained rather than ILike, so the same query runs on every provider the tests use.
            string term = search.ToLowerInvariant();
            items = items.Where(item =>
                item.Title.ToLower().Contains(term)
                || (item.Brand != null && item.Brand.ToLower().Contains(term))
                || (item.Barcode != null && item.Barcode.ToLower().Contains(term))
                || (item.StorageLocation != null && item.StorageLocation.ToLower().Contains(term)));
        }

        List<MatchPoolItem> pool = await Project(items.OrderByDescending(item => item.UpdatedAt).Take(take)).ToListAsync(cancellationToken);
        Guid[] missing = [.. alwaysInclude.Where(id => pool.All(item => item.Id != id))];
        if (missing.Length > 0)
        {
            pool.AddRange(await Project(db.InventoryItems.AsNoTracking().Where(item => item.OrganizationId == organizationId && missing.Contains(item.Id)))
                .ToListAsync(cancellationToken));
        }

        return pool;
    }

    public async Task<IReadOnlySet<Guid>> ItemsWithExternalListingAsync(Guid organizationId, string externalListingId, CancellationToken cancellationToken) =>
        (await db.ChannelListings.AsNoTracking()
            .Where(channel => channel.OrganizationId == organizationId && channel.ExternalListingId == externalListingId)
            .Select(channel => channel.InventoryItemId)
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlySet<Guid>> ItemsListedElsewhereOnChannelAsync(
        Guid organizationId,
        string channel,
        string externalListingId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        string lowered = channel.ToLower();
        return (await db.ChannelListings.AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId
                && itemIds.Contains(entry.InventoryItemId)
                && entry.ExternalListingId != null
                && entry.ExternalListingId != externalListingId
                && entry.ChannelAccount != null
                && entry.ChannelAccount.Channel.ToLower() == lowered)
            .Select(entry => entry.InventoryItemId)
            .ToListAsync(cancellationToken)).ToHashSet();
    }

    private static IQueryable<MatchPoolItem> Project(IQueryable<InventoryItem> items) =>
        items.Select(item => new MatchPoolItem(
            item.Id,
            item.Title,
            item.Brand,
            item.Category,
            item.Colors,
            item.SizeSystem,
            item.SizeValue,
            item.EstimatedSalePrice,
            item.Status,
            item.StorageLocation,
            item.Barcode,
            item.Photos.OrderByDescending(photo => photo.IsHero).ThenBy(photo => photo.SortOrder).Select(photo => (Guid?)photo.Id).FirstOrDefault()));
}
