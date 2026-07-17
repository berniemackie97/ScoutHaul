using Microsoft.EntityFrameworkCore;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>EF role lookup helper for organization workflows that still live in API.</summary>
public sealed class OrganizationRoleGuard(HaulScoutDbContext db)
{
    public Task<OrganizationRoleDefinition?> LoadRoleAsync(Guid organizationId, Guid roleId, CancellationToken cancellationToken) =>
        db.OrganizationRoles
            .Include(role => role.Permissions)
            .SingleOrDefaultAsync(role => role.Id == roleId && role.OrganizationId == organizationId, cancellationToken);

    public Task<OrganizationRoleDefinition?> LoadOwnerRoleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == organizationId && role.IsOwnerRole)
            .OrderBy(role => role.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<OrganizationRoleDefinition?> LoadDefaultInviteRoleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == organizationId && role.IsSystem && !role.IsOwnerRole)
            .OrderBy(role => role.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> TeamBelongsToOrganizationAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken) =>
        db.OrganizationTeams.AnyAsync(team => team.Id == teamId && team.OrganizationId == organizationId, cancellationToken);
}
