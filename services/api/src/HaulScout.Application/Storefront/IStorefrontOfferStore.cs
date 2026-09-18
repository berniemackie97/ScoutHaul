using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

public interface IStorefrontOfferStore
{
    Task<IReadOnlyList<StorefrontOffer>> ListOffersAsync(
        Guid organizationId,
        StorefrontOfferStatus? status,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many offers are in each status, over every offer rather than the page the list returns.
    /// The offers screen's "new offers" figure used to be counted from the tab being viewed — so on
    /// the Declined tab it read zero however many buyers were waiting.
    /// </summary>
    Task<IReadOnlyDictionary<StorefrontOfferStatus, int>> CountByStatusAsync(Guid organizationId, CancellationToken cancellationToken);

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
