using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

/// <summary>
/// The organization itself: the ones you belong to, and creating a new one. A new organization is bootstrapped with
/// its owner and operator roles and its creator as owner — otherwise nobody could administer it.
/// </summary>
public sealed class OrganizationWorkflowService(
    IOrganizationStore organizations,
    ICurrentUserContext currentUser,
    IOrganizationAuthorizer authorizer)
{
    public async Task<IReadOnlyList<OrganizationDto>> ListMineAsync(CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        IReadOnlyList<Organization> records = await organizations.ListForUserAsync(currentUser.UserId, cancellationToken);

        return records.Select(organization => organization.ToDto()).ToArray();
    }

    public async Task<AppResult<OrganizationDto>> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationDto>.Validation("Organization name is required.");
        }

        AppUser user = await organizations.FindUserAsync(currentUser.UserId, cancellationToken)
                       ?? throw new InvalidOperationException("User must be resolved before creating an organization.");

        Organization organization = new() { Name = request.Name.Trim() };
        OrganizationRoleDefinition ownerRole = OrganizationRoleDefaults.CreateOwnerRole(organization.Id);
        OrganizationRoleDefinition operatorRole = OrganizationRoleDefaults.CreateOperatorRole(organization.Id);
        organization.Roles.Add(ownerRole);
        organization.Roles.Add(operatorRole);
        organization.Members.Add(new OrganizationMember
        {
            UserId = user.Id,
            RoleId = ownerRole.Id,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow
        });

        await organizations.AddAsync(organization, cancellationToken);
        await organizations.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationDto>.Ok(organization.ToDto());
    }

    public async Task<AppResult<OrganizationDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ViewReports, cancellationToken);
        Organization? organization = await organizations.FindAsync(id, cancellationToken);
        return organization is null
            ? AppResult<OrganizationDto>.NotFound()
            : AppResult<OrganizationDto>.Ok(organization.ToDto());
    }
}
