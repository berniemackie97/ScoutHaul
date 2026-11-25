using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

public sealed class EfInventoryListingDifferenceStore(HaulScoutDbContext db) : IInventoryListingDifferenceStore
{
    public Task<InventoryItem?> FindItemAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        db.InventoryItems.FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == itemId, cancellationToken);

    public async Task<IReadOnlyList<PlatformListing>> ListLinkedListingsAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        await db.PlatformListings
            .AsNoTracking()
            .Where(listing => listing.OrganizationId == organizationId && listing.ReconciledInventoryItemId == itemId)
            .OrderByDescending(listing => listing.UpdatedAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
