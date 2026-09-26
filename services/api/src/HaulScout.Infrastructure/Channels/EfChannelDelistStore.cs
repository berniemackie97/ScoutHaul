using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Channels;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Channels;

public sealed class EfChannelDelistStore(HaulScoutDbContext db) : IChannelDelistStore
{
    public async Task<IReadOnlyList<ChannelListing>> ListPendingAsync(Guid organizationId, int take, CancellationToken cancellationToken) =>
        await Pending(organizationId)
            .Include(listing => listing.ChannelAccount)
            .Include(listing => listing.InventoryItem)
            .OrderBy(listing => listing.LastSyncedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountPendingAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Pending(organizationId).CountAsync(cancellationToken);

    public async Task<ChannelListing?> FindAsync(Guid organizationId, Guid channelListingId, CancellationToken cancellationToken) =>
        await db.ChannelListings
            .Include(listing => listing.ChannelAccount)
            .Include(listing => listing.InventoryItem)
            .SingleOrDefaultAsync(
                listing => listing.Id == channelListingId && listing.OrganizationId == organizationId,
                cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ItemSale>> SoldOnAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> inventoryItemIds,
        CancellationToken cancellationToken)
    {
        // The most recent sale of each item that still stands: a cancelled one is not why a listing must come down.
        var sales = await db.Sales
            .Where(sale => sale.OrganizationId == organizationId
                && inventoryItemIds.Contains(sale.InventoryItemId)
                && sale.Status != SaleStatus.Cancelled)
            .Select(sale => new { sale.InventoryItemId, sale.Platform, sale.SoldAt })
            .ToListAsync(cancellationToken);

        return sales
            .GroupBy(sale => sale.InventoryItemId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(sale => sale.SoldAt).Select(sale => new ItemSale(sale.Platform, sale.SoldAt)).First());
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private IQueryable<ChannelListing> Pending(Guid organizationId) =>
        db.ChannelListings.Where(listing =>
            listing.OrganizationId == organizationId && listing.Status == ChannelListingStatus.PendingDelist);
}
