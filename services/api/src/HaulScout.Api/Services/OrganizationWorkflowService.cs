using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// The organization itself: the ones you belong to, and creating a new one. A new organization is bootstrapped with
/// its owner and operator roles and its creator as owner — otherwise nobody could administer it.
/// </summary>
public sealed class OrganizationWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    IOrganizationAuthorizer access)
{
    public async Task<IReadOnlyList<OrganizationDto>> ListMineAsync(CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        List<OrganizationMember> memberships = await db.OrganizationMembers
            .Include(member => member.Organization)
            .Where(member => member.UserId == currentUser.UserId)
            .OrderBy(member => member.Organization!.Name)
            .ToListAsync(cancellationToken);

        return memberships
            .Where(member => member.Organization is not null)
            .Select(member => member.Organization!.ToDto())
            .ToArray();
    }

    public async Task<AppResult<OrganizationDto>> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationDto>.Validation("Organization name is required.");
        }

        AppUser user = await db.Users.FindAsync([currentUser.UserId], cancellationToken)
                       ?? throw new InvalidOperationException("User must be resolved before creating an organization.");

        Organization organization = new() { Name = request.Name.Trim() };
        db.Organizations.Add(organization);
        OrganizationRoleDefinition ownerRole = OrganizationRoleDefaults.CreateOwnerRole(organization.Id);
        OrganizationRoleDefinition operatorRole = OrganizationRoleDefaults.CreateOperatorRole(organization.Id);
        db.OrganizationRoles.AddRange(ownerRole, operatorRole);
        organization.Members.Add(new OrganizationMember
        {
            UserId = user.Id,
            RoleId = ownerRole.Id,
            Role = Domain.OrganizationRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationDto>.Ok(organization.ToDto());
    }

    public async Task<AppResult<OrganizationDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ViewReports, cancellationToken);
        Organization? organization = await db.Organizations.FindAsync([id], cancellationToken);
        return organization is null
            ? AppResult<OrganizationDto>.NotFound()
            : AppResult<OrganizationDto>.Ok(organization.ToDto());
    }
}
