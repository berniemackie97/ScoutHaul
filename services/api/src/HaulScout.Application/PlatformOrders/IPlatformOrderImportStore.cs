using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

public interface IPlatformOrderImportStore
{
    Task<bool> ChannelAccountExistsAsync(Guid organizationId, Guid channelAccountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListKnownSalesChannelsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListKnownListingChannelsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformOrder>> ListImportedOrdersByExternalIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<string> externalOrderIds,
        CancellationToken cancellationToken);

    /// <summary>Orders placed by any of these buyers, with their lines.</summary>
    Task<IReadOnlyList<PlatformOrder>> ListOrdersByBuyersAsync(
        Guid organizationId,
        IReadOnlyCollection<string> buyerReferences,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformOrder>> ListImportedOrdersForDisplayAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken);

    void AddPlatformOrder(PlatformOrder order);

    void ReplacePlatformOrderLines(PlatformOrder order, IReadOnlyCollection<PlatformOrderLine> replacementLines);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
