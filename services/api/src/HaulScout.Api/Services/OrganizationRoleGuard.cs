using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// The rules that decide when an action needs an owner rather than a permission. Members, invite codes, roles, and
/// teams all hand out access, so they all consult this — a single definition of "this grants owner-level power"
/// is what stops one surface drifting looser than the others.
/// </summary>
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

    /// <summary>Trims, de-duplicates, and rejects anything outside the known permission catalog.</summary>
    public static string[] ValidatePermissions(IEnumerable<string> permissions)
    {
        string[] normalized = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string? unknown = normalized.FirstOrDefault(permission => !HaulScoutPermissions.IsKnown(permission));
        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown permission '{unknown}'.");
        }

        return normalized;
    }

    public static void ReplacePermissions(OrganizationRoleDefinition role, IReadOnlyList<string> permissions)
    {
        role.Permissions.Clear();
        foreach (string permission in permissions)
        {
            role.Permissions.Add(new OrganizationRolePermission { RoleId = role.Id, Permission = permission });
        }
    }

    /// <summary>A role is owner-level if it is the owner role, or if it carries any owner-guarded permission.</summary>
    public static bool RoleRequiresOwner(OrganizationRoleDefinition? role) =>
        role?.IsOwnerRole == true || role?.Permissions.Any(permission => IsOwnerGuardedPermission(permission.Permission)) == true;

    public static bool ContainsOwnerGuardedPermission(IEnumerable<string> permissions) =>
        permissions.Any(IsOwnerGuardedPermission);

    /// <summary>The permissions that let a member reshape access itself — only an owner may hand these out.</summary>
    public static bool IsOwnerGuardedPermission(string permission) =>
        permission is HaulScoutPermissions.ManageOrganization
            or HaulScoutPermissions.ManageOwners
            or HaulScoutPermissions.ManageRoles;
}
