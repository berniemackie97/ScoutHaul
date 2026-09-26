using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Channels;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Channels;

public sealed class EfSoldListingStore(HaulScoutDbContext db) : ISoldListingStore
{
    public async Task<IReadOnlyList<PlatformListing>> ListSoldUnrecordedAsync(Guid organizationId, int take, CancellationToken cancellationToken) =>
        await Unrecorded(organizationId)
            .Include(listing => listing.ReconciledInventoryItem)
            .OrderBy(listing => listing.MarkedSoldAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountSoldUnrecordedAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Unrecorded(organizationId).CountAsync(cancellationToken);

    public async Task<PlatformListing?> FindUnrecordedAsync(Guid organizationId, Guid platformListingId, CancellationToken cancellationToken) =>
        await Unrecorded(organizationId)
            .Include(listing => listing.ReconciledInventoryItem)
            .SingleOrDefaultAsync(listing => listing.Id == platformListingId, cancellationToken);

    public Task<bool> ExistsAsync(Guid organizationId, Guid platformListingId, CancellationToken cancellationToken) =>
        db.PlatformListings.AnyAsync(listing => listing.Id == platformListingId && listing.OrganizationId == organizationId, cancellationToken);

    /// <summary>Sold, linked, unanswered, and holding no sale that still stands — a cancelled one is not an answer.</summary>
    private IQueryable<PlatformListing> Unrecorded(Guid organizationId) =>
        db.PlatformListings.Where(listing =>
            listing.OrganizationId == organizationId
            && listing.Status == PlatformListingStatus.Sold
            && listing.ReconciledInventoryItemId != null
            && listing.SoldAcknowledgedAt == null
            && !db.Sales.Any(sale =>
                sale.OrganizationId == organizationId
                && sale.InventoryItemId == listing.ReconciledInventoryItemId
                && sale.Status != SaleStatus.Cancelled));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
