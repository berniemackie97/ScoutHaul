using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public interface IOrganizationRoleStore
{
    Task<IReadOnlyList<OrganizationRoleDefinition>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<OrganizationRoleDefinition?> FindAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken);

    Task<OrganizationRoleDefinition?> FindOwnerRoleAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<OrganizationRoleDefinition?> FindDefaultInviteRoleAsync(Guid organizationId, CancellationToken cancellationToken);

    Task AddAsync(OrganizationRoleDefinition role, CancellationToken cancellationToken);

    void Remove(OrganizationRoleDefinition role);

    Task<bool> IsAssignedAsync(Guid roleId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
