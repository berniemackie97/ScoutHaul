using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

public sealed class OrganizationAccessService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<OrganizationMember> RequirePermission(
        Guid organizationId,
        string permission,
        CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        OrganizationMember? membership = await db.OrganizationMembers
            .Include(item => item.RoleDefinition)
                .ThenInclude(role => role!.Permissions)
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.UserId == currentUser.UserId,
                cancellationToken);

        if (membership is null)
        {
            throw new UnauthorizedAccessException("User is not a member of this organization.");
        }

        if (HasPermission(membership, permission))
        {
            return membership;
        }

        throw new UnauthorizedAccessException($"Missing required permission '{permission}'.");
    }

    public async Task<OrganizationMember> RequireOwner(Guid organizationId, CancellationToken cancellationToken)
    {
        OrganizationMember membership = await RequirePermission(organizationId, HaulScoutPermissions.ManageOrganization, cancellationToken);
        if (membership.Role == OrganizationRole.Owner || membership.RoleDefinition?.IsOwnerRole == true)
        {
            return membership;
        }

        throw new UnauthorizedAccessException("Only an owner can perform this action.");
    }

    public static bool HasPermission(OrganizationMember membership, string permission)
    {
        if (membership.Role == OrganizationRole.Owner || membership.RoleDefinition?.IsOwnerRole == true)
        {
            return true;
        }

        return membership.RoleDefinition?.Permissions.Any(item => item.Permission == permission) == true;
    }
}
