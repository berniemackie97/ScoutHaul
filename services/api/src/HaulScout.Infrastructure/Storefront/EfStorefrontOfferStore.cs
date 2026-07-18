using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

public sealed class EfStorefrontOfferStore(HaulScoutDbContext db) : IStorefrontOfferStore
{
    public async Task<IReadOnlyList<StorefrontOffer>> ListOffersAsync(
        Guid organizationId,
        StorefrontOfferStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<StorefrontOffer> query = db.StorefrontOffers
            .Include(offer => offer.StoreListing)
            .Include(offer => offer.InventoryItem)
            .Where(offer => offer.OrganizationId == organizationId);

        if (status is not null)
        {
            query = query.Where(offer => offer.Status == status);
        }

        return await query
            .OrderByDescending(offer => offer.OfferedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken) =>
        db.StoreListings
            .Include(listing => listing.InventoryItem)
            .SingleOrDefaultAsync(
                listing => listing.Id == storeListingId && listing.OrganizationId == organizationId,
                cancellationToken);

    public async Task AddOfferAsync(StorefrontOffer offer, CancellationToken cancellationToken) =>
        await db.StorefrontOffers.AddAsync(offer, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
