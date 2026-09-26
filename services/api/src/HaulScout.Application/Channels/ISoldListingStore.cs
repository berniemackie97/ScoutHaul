using HaulScout.Domain.Entities;

namespace HaulScout.Application.Channels;

/// <summary>Listings a marketplace says have sold, for items the books still show as unsold.</summary>
public interface ISoldListingStore
{
    Task<IReadOnlyList<PlatformListing>> ListSoldUnrecordedAsync(Guid organizationId, int take, CancellationToken cancellationToken);

    Task<int> CountSoldUnrecordedAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The one the queue is asking about, or null when it is not asking about it.</summary>
    Task<PlatformListing?> FindUnrecordedAsync(Guid organizationId, Guid platformListingId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid organizationId, Guid platformListingId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
