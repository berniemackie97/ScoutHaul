using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

/// <summary>
/// Custom roles and the permissions they carry. Anything that would grant owner-level power demands an owner —
/// otherwise a member with roles.manage could quietly promote themselves. System and owner roles are protected.
/// </summary>
public sealed class OrganizationRoleWorkflowService(
    IOrganizationRoleStore roles,
    IOrganizationAuthorizer authorizer)
{
    public async Task<IReadOnlyList<OrganizationRoleDto>> ListAsync(Guid id, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        IReadOnlyList<OrganizationRoleDefinition> records = await roles.ListAsync(id, cancellationToken);
        return records.Select(role => role.ToDto()).ToArray();
    }

    public async Task<AppResult<OrganizationRoleDto>> CreateAsync(Guid id, UpsertOrganizationRoleRequest request, CancellationToken cancellationToken)
    {
        string[] permissions;
        try
        {
            permissions = OrganizationRoleRules.ValidatePermissions(
                request.GrantsOwnerAccess ? HaulScoutPermissions.All : request.Permissions);
        }
        catch (ArgumentException e)
        {
            return AppResult<OrganizationRoleDto>.Validation(e.Message);
        }
        if (permissions.Length == 0)
        {
            return AppResult<OrganizationRoleDto>.Validation("At least one permission is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationRoleDto>.Validation("Role name is required.");
        }

        if (request.GrantsOwnerAccess || OrganizationRoleRules.ContainsOwnerGuardedPermission(permissions))
        {
            await authorizer.RequireOwner(id, cancellationToken);
        }
        else
        {
            await authorizer.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        }

        OrganizationRoleDefinition role = new()
        {
            OrganizationId = id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsOwnerRole = request.GrantsOwnerAccess,
            IsSystem = false
        };

        OrganizationRoleRules.ReplacePermissions(role, permissions);
        await roles.AddAsync(role, cancellationToken);
        await roles.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationRoleDto>.Ok(role.ToDto());
    }

    public async Task<AppResult<OrganizationRoleDto>> UpdateAsync(Guid id, Guid roleId, UpsertOrganizationRoleRequest request, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = await roles.FindAsync(id, roleId, cancellationToken);

        if (role is null)
        {
            return AppResult<OrganizationRoleDto>.NotFound();
        }
        if (role.IsSystem)
        {
            return AppResult<OrganizationRoleDto>.Validation("System roles are protected. Create a custom role instead.");
        }

        string[] permissions;
        try
        {
            permissions = OrganizationRoleRules.ValidatePermissions(
                request.GrantsOwnerAccess ? HaulScoutPermissions.All : request.Permissions);
        }
        catch (ArgumentException e)
        {
            return AppResult<OrganizationRoleDto>.Validation(e.Message);
        }
        if (permissions.Length == 0)
        {
            return AppResult<OrganizationRoleDto>.Validation("At least one permission is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationRoleDto>.Validation("Role name is required.");
        }

        // Editing an owner role is itself owner-only, even when the new permission set looks ordinary.
        if (request.GrantsOwnerAccess || role.IsOwnerRole || OrganizationRoleRules.ContainsOwnerGuardedPermission(permissions))
        {
            await authorizer.RequireOwner(id, cancellationToken);
        }
        else
        {
            await authorizer.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        }

        role.Name = request.Name.Trim();
        role.Description = request.Description?.Trim();
        role.IsOwnerRole = request.GrantsOwnerAccess;
        OrganizationRoleRules.ReplacePermissions(role, permissions);

        await roles.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationRoleDto>.Ok(role.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = await roles.FindAsync(id, roleId, cancellationToken);

        if (role is null)
        {
            return AppResult.NotFound();
        }
        if (role.IsSystem || role.IsOwnerRole)
        {
            return AppResult.Validation("System and owner roles cannot be deleted.");
        }
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);

        bool isAssigned = await roles.IsAssignedAsync(roleId, cancellationToken);
        if (isAssigned)
        {
            return AppResult.Validation("Role is assigned to members or teams. Reassign them before deleting it.");
        }

        roles.Remove(role);
        await roles.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }
}
