using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Organizations;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Organizations;

public sealed class EfOrganizationRoleStore(HaulScoutDbContext db) : IOrganizationRoleStore
{
    public async Task<IReadOnlyList<OrganizationRoleDefinition>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == organizationId)
            .OrderByDescending(role => role.IsOwnerRole)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);

    public Task<OrganizationRoleDefinition?> FindAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken) =>
        db.OrganizationRoles
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId && item.OrganizationId == organizationId, cancellationToken);

    public Task<OrganizationRoleDefinition?> FindOwnerRoleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == organizationId && role.IsOwnerRole)
            .OrderBy(role => role.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(OrganizationRoleDefinition role, CancellationToken cancellationToken) =>
        await db.OrganizationRoles.AddAsync(role, cancellationToken);

    public void Remove(OrganizationRoleDefinition role) => db.OrganizationRoles.Remove(role);

    public async Task<bool> IsAssignedAsync(Guid roleId, CancellationToken cancellationToken) =>
        await db.OrganizationMembers.AnyAsync(item => item.RoleId == roleId, cancellationToken)
        || await db.OrganizationTeamMembers.AnyAsync(item => item.RoleId == roleId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
