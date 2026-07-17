using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Organizations;

public sealed class EfOrganizationAuthorizer(HaulScoutDbContext db, ICurrentUserContext currentUser) : IOrganizationAuthorizer
{
    public async Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken)
    {
        OrganizationMember membership = await LoadMembership(organizationId, cancellationToken);

        if (HasPermission(membership, permission))
        {
            return;
        }

        throw new UnauthorizedAccessException($"Missing required permission '{permission}'.");
    }

    public async Task RequireOwner(Guid organizationId, CancellationToken cancellationToken)
    {
        OrganizationMember membership = await LoadMembership(organizationId, cancellationToken);

        if (membership.Role == OrganizationRole.Owner || membership.RoleDefinition?.IsOwnerRole == true)
        {
            return;
        }

        throw new UnauthorizedAccessException("Only an owner can perform this action.");
    }

    private async Task<OrganizationMember> LoadMembership(Guid organizationId, CancellationToken cancellationToken)
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

        return membership;
    }

    private static bool HasPermission(OrganizationMember membership, string permission)
    {
        if (membership.Role == OrganizationRole.Owner || membership.RoleDefinition?.IsOwnerRole == true)
        {
            return true;
        }

        return membership.RoleDefinition?.Permissions.Any(item => item.Permission == permission) == true;
    }
}
