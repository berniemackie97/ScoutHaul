using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

/// <summary>
/// Shared rules for role permission sets. Any surface that grants access must use these rules so owner-level
/// escalation is guarded consistently.
/// </summary>
public static class OrganizationRoleRules
{
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
