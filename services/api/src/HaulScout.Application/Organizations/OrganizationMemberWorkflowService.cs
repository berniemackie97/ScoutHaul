using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

/// <summary>
/// Who belongs to an organization and what they are. Handing out — or taking away — owner-level membership is
/// owner-only, and an organization can never be left without an owner.
/// </summary>
public sealed class OrganizationMemberWorkflowService(
    IOrganizationMemberStore members,
    IOrganizationRoleStore roles,
    IOrganizationAuthorizer authorizer,
    IOrganizationInviteSender invites)
{
    public async Task<IReadOnlyList<OrganizationMemberDto>> ListAsync(Guid id, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        IReadOnlyList<OrganizationMember> records = await members.ListAsync(id, cancellationToken);
        return records.Select(member => member.ToDto()).ToArray();
    }

    public async Task<AppResult<OrganizationMemberDto>> AddAsync(Guid id, AddOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? requestedRole = null;
        if (request.RoleId is not null)
        {
            requestedRole = await roles.FindAsync(id, request.RoleId.Value, cancellationToken);
            if (requestedRole is null)
            {
                return AppResult<OrganizationMemberDto>.Validation("Role does not belong to this organization.");
            }
        }

        bool ownerRoleRequested = request.Role == OrganizationRole.Owner || requestedRole?.IsOwnerRole == true;
        if (ownerRoleRequested || OrganizationRoleRules.RoleRequiresOwner(requestedRole))
        {
            await authorizer.RequireOwner(id, cancellationToken);
        }
        else
        {
            await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return AppResult<OrganizationMemberDto>.Validation("Email and display name are required.");
        }

        Organization? organization = await members.FindOrganizationAsync(id, cancellationToken);
        if (organization is null)
        {
            return AppResult<OrganizationMemberDto>.NotFound();
        }

        string email = request.Email.Trim().ToLowerInvariant();
        AppUser? user = await members.FindUserByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            user = new AppUser { Email = email, DisplayName = request.DisplayName.Trim() };
            await members.AddUserAsync(user, cancellationToken);
        }

        if (await members.MemberExistsAsync(id, user.Id, cancellationToken))
        {
            return AppResult<OrganizationMemberDto>.Conflict("User is already a member of this organization.");
        }

        OrganizationRoleDefinition? ownerRole = ownerRoleRequested && requestedRole is null
            ? await roles.FindOwnerRoleAsync(id, cancellationToken)
            : null;
        if (ownerRoleRequested && requestedRole is null && ownerRole is null)
        {
            return AppResult<OrganizationMemberDto>.Validation("Owner role is not configured for this organization.");
        }

        OrganizationMember member = new()
        {
            OrganizationId = id,
            UserId = user.Id,
            RoleId = requestedRole?.Id ?? ownerRole?.Id,
            Role = ownerRoleRequested ? OrganizationRole.Owner : request.Role,
            JoinedAt = DateTimeOffset.UtcNow
        };

        await members.AddMemberAsync(member, cancellationToken);
        await members.SaveChangesAsync(cancellationToken);
        _ = await invites.SendInviteAsync(user.Email, user.DisplayName, id, organization.Name, cancellationToken);
        member.User = user;
        member.RoleDefinition = requestedRole ?? ownerRole;

        return AppResult<OrganizationMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult<OrganizationMemberDto>> ResendInviteAsync(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        OrganizationMember? member = await members.FindMemberWithUserAndRoleAsync(id, memberId, cancellationToken);
        if (member?.User is null)
        {
            return AppResult<OrganizationMemberDto>.NotFound();
        }
        if (!string.IsNullOrWhiteSpace(member.User.ExternalSubject))
        {
            return AppResult<OrganizationMemberDto>.Validation("Member already has an active authentication account.");
        }

        Organization? organization = await members.FindOrganizationAsync(id, cancellationToken);
        if (organization is null)
        {
            return AppResult<OrganizationMemberDto>.NotFound();
        }

        _ = await invites.SendInviteAsync(member.User.Email, member.User.DisplayName, id, organization.Name, cancellationToken);
        return AppResult<OrganizationMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult<OrganizationMemberDto>> UpdateRoleAsync(Guid id, Guid memberId, UpdateOrganizationMemberRoleRequest request, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = null;
        if (request.RoleId is not null)
        {
            role = await roles.FindAsync(id, request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationMemberDto>.Validation("Role does not belong to this organization.");
            }
        }

        bool ownerRoleRequested = role?.IsOwnerRole == true || request.LegacyRole == OrganizationRole.Owner;
        if (ownerRoleRequested || OrganizationRoleRules.RoleRequiresOwner(role))
        {
            await authorizer.RequireOwner(id, cancellationToken);
        }
        else
        {
            await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        }

        OrganizationMember? member = await members.FindMemberWithUserAndRoleAsync(id, memberId, cancellationToken);
        if (member is null)
        {
            return AppResult<OrganizationMemberDto>.NotFound();
        }

        if (ownerRoleRequested && role is null)
        {
            role = await roles.FindOwnerRoleAsync(id, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationMemberDto>.Validation("Owner role is not configured for this organization.");
            }
        }

        member.RoleId = role?.Id;
        member.RoleDefinition = role;
        member.Role = ownerRoleRequested ? OrganizationRole.Owner : request.LegacyRole ?? OrganizationRole.Member;
        await members.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult> RemoveAsync(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        OrganizationMember? member = await members.FindMemberWithRoleAsync(id, memberId, cancellationToken);
        if (member is null)
        {
            return AppResult.NotFound();
        }

        bool removingOwner = member.Role == OrganizationRole.Owner || member.RoleDefinition?.IsOwnerRole == true;
        if (removingOwner)
        {
            await authorizer.RequireOwner(id, cancellationToken);

            int ownerCount = await members.CountOwnersAsync(id, cancellationToken);
            if (ownerCount <= 1)
            {
                return AppResult.Validation("An organization must keep at least one owner.");
            }
        }
        else
        {
            await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        }

        IReadOnlyList<OrganizationTeamMember> teamMemberships =
            await members.ListTeamMembershipsForUserAsync(id, member.UserId, cancellationToken);
        members.RemoveTeamMemberships(teamMemberships);
        members.Remove(member);
        await members.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }
}
