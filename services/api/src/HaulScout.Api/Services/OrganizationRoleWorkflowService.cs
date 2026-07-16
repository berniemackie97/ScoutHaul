using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Custom roles and the permissions they carry. Anything that would grant owner-level power demands an owner —
/// otherwise a member with roles.manage could quietly promote themselves. System and owner roles are protected.
/// </summary>
public sealed class OrganizationRoleWorkflowService(
    HaulScoutDbContext db,
    OrganizationAccessService access,
    OrganizationRoleGuard guard)
{
    public async Task<IReadOnlyList<OrganizationRoleDto>> ListAsync(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        List<OrganizationRoleDefinition> roles = await db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == id)
            .OrderByDescending(role => role.IsOwnerRole)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(role => role.ToDto()).ToArray();
    }

    public async Task<AppResult<OrganizationRoleDto>> CreateAsync(Guid id, UpsertOrganizationRoleRequest request, CancellationToken cancellationToken)
    {
        string[] permissions;
        try
        {
            permissions = OrganizationRoleGuard.ValidatePermissions(
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

        if (request.GrantsOwnerAccess || OrganizationRoleGuard.ContainsOwnerGuardedPermission(permissions))
        {
            await access.RequireOwner(id, cancellationToken);
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        }

        OrganizationRoleDefinition role = new()
        {
            OrganizationId = id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsOwnerRole = request.GrantsOwnerAccess,
            IsSystem = false
        };

        OrganizationRoleGuard.ReplacePermissions(role, permissions);
        db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationRoleDto>.Ok(role.ToDto());
    }

    public async Task<AppResult<OrganizationRoleDto>> UpdateAsync(Guid id, Guid roleId, UpsertOrganizationRoleRequest request, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = await db.OrganizationRoles
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId && item.OrganizationId == id, cancellationToken);

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
            permissions = OrganizationRoleGuard.ValidatePermissions(
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
        if (request.GrantsOwnerAccess || role.IsOwnerRole || OrganizationRoleGuard.ContainsOwnerGuardedPermission(permissions))
        {
            await access.RequireOwner(id, cancellationToken);
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        }

        role.Name = request.Name.Trim();
        role.Description = request.Description?.Trim();
        role.IsOwnerRole = request.GrantsOwnerAccess;
        OrganizationRoleGuard.ReplacePermissions(role, permissions);

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationRoleDto>.Ok(role.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = await db.OrganizationRoles
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId && item.OrganizationId == id, cancellationToken);

        if (role is null)
        {
            return AppResult.NotFound();
        }
        if (role.IsSystem || role.IsOwnerRole)
        {
            return AppResult.Validation("System and owner roles cannot be deleted.");
        }
        await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);

        bool isAssigned = await db.OrganizationMembers.AnyAsync(item => item.RoleId == roleId, cancellationToken)
            || await db.OrganizationTeamMembers.AnyAsync(item => item.RoleId == roleId, cancellationToken);
        if (isAssigned)
        {
            return AppResult.Validation("Role is assigned to members or teams. Reassign them before deleting it.");
        }

        db.OrganizationRoles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }
}
