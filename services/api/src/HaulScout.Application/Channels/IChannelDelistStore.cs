using HaulScout.Domain.Entities;

namespace HaulScout.Application.Channels;

/// <summary>
/// The listings a sale left behind: still live on a marketplace for an item that has already gone. Nothing is
/// removed from any marketplace from here — a person does that, on the marketplace — so this finds them and records
/// what the person did.
/// </summary>
public interface IChannelDelistStore
{
    Task<IReadOnlyList<ChannelListing>> ListPendingAsync(Guid organizationId, int take, CancellationToken cancellationToken);

    Task<int> CountPendingAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<ChannelListing?> FindAsync(Guid organizationId, Guid channelListingId, CancellationToken cancellationToken);

    /// <summary>When each item sold and where, so the queue can say why a listing has to come down.</summary>
    Task<IReadOnlyDictionary<Guid, ItemSale>> SoldOnAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> inventoryItemIds,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Where and when an item sold, as far as the books know.</summary>
public sealed record ItemSale(string? Channel, DateTimeOffset? SoldAt);
