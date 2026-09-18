using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

public sealed class EfStorefrontOfferStore(HaulScoutDbContext db) : IStorefrontOfferStore
{
    public Task<int> CountOffersAsync(Guid organizationId, StorefrontOfferStatus? status, CancellationToken cancellationToken) =>
        OffersIn(organizationId, status).CountAsync(cancellationToken);

    /// <summary>The one definition of "offers in this status", shared by the list and the count.</summary>
    private IQueryable<StorefrontOffer> OffersIn(Guid organizationId, StorefrontOfferStatus? status)
    {
        IQueryable<StorefrontOffer> query = db.StorefrontOffers.Where(offer => offer.OrganizationId == organizationId);
        return status is null ? query : query.Where(offer => offer.Status == status);
    }

    public async Task<IReadOnlyList<StorefrontOffer>> ListOffersAsync(
        Guid organizationId,
        StorefrontOfferStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<StorefrontOffer> query = OffersIn(organizationId, status)
            .Include(offer => offer.Customer)
            .Include(offer => offer.StoreListing)
            .Include(offer => offer.InventoryItem);

        return await query
            .OrderByDescending(offer => offer.OfferedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public Task<StorefrontOffer?> FindOfferAsync(Guid organizationId, Guid offerId, CancellationToken cancellationToken) =>
        OfferQuery(organizationId)
            .SingleOrDefaultAsync(offer => offer.Id == offerId, cancellationToken);

    public async Task<IReadOnlyList<StorefrontOffer>> ListCustomerOffersAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken) =>
        await OfferQuery(organizationId)
            .Where(offer => offer.CustomerId == customerId)
            .OrderByDescending(offer => offer.OfferedAt)
            .ToListAsync(cancellationToken);

    public Task<StorefrontOffer?> FindCustomerOfferAsync(
        Guid organizationId,
        Guid customerId,
        Guid offerId,
        CancellationToken cancellationToken) =>
        OfferQuery(organizationId)
            .SingleOrDefaultAsync(
                offer => offer.Id == offerId && offer.CustomerId == customerId,
                cancellationToken);

    public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken) =>
        db.StoreListings
            .Include(listing => listing.InventoryItem)
            .SingleOrDefaultAsync(
                listing => listing.Id == storeListingId && listing.OrganizationId == organizationId,
                cancellationToken);

    public async Task AddOfferAsync(StorefrontOffer offer, CancellationToken cancellationToken) =>
        await db.StorefrontOffers.AddAsync(offer, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private IQueryable<StorefrontOffer> OfferQuery(Guid organizationId) =>
        db.StorefrontOffers
            .Include(offer => offer.Customer)
            .Include(offer => offer.StoreListing)
            .Include(offer => offer.InventoryItem)
            .Where(offer => offer.OrganizationId == organizationId);
}
