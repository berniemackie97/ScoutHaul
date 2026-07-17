using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

public interface IStoreSettingsStore
{
    Task<Organization?> FindAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<bool> StoreDomainExistsForAnotherOrganizationAsync(
        string storeDomain,
        Guid organizationId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
