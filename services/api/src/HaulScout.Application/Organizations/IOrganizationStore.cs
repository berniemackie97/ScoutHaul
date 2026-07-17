using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public interface IOrganizationStore
{
    Task<IReadOnlyList<Organization>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<AppUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<Organization?> FindAsync(Guid organizationId, CancellationToken cancellationToken);

    Task AddAsync(Organization organization, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
