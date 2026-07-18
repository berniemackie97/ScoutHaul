using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

public interface IStorefrontOfferStore
{
    Task<IReadOnlyList<StorefrontOffer>> ListOffersAsync(
        Guid organizationId,
        StorefrontOfferStatus? status,
        CancellationToken cancellationToken);

    Task<StorefrontOffer?> FindOfferAsync(Guid organizationId, Guid offerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StorefrontOffer>> ListCustomerOffersAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken);

    Task<StorefrontOffer?> FindCustomerOfferAsync(
        Guid organizationId,
        Guid customerId,
        Guid offerId,
        CancellationToken cancellationToken);

    Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken);

    Task AddOfferAsync(StorefrontOffer offer, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
