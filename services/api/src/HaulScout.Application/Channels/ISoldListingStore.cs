using HaulScout.Domain.Entities;

namespace HaulScout.Application.Channels;

/// <summary>Listings a marketplace says have sold, for items the books still show as unsold.</summary>
public interface ISoldListingStore
{
    Task<IReadOnlyList<PlatformListing>> ListSoldUnrecordedAsync(Guid organizationId, int take, CancellationToken cancellationToken);

    Task<int> CountSoldUnrecordedAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<PlatformListing?> FindAsync(Guid organizationId, Guid platformListingId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
