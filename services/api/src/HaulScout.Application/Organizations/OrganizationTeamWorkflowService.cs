using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

/// <summary>
/// Teams and their membership. A team is a grouping inside one organization, so you must already belong to the
/// organization to join one — and a team role that would grant owner-level power still needs an owner.
/// </summary>
public sealed class OrganizationTeamWorkflowService(
    IOrganizationTeamStore teams,
    IOrganizationRoleStore roles,
    IOrganizationAuthorizer authorizer)
{
    public async Task<IReadOnlyList<OrganizationTeamDto>> ListAsync(Guid id, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        IReadOnlyList<OrganizationTeam> records = await teams.ListAsync(id, cancellationToken);
        return records.Select(team => team.ToDto()).ToArray();
    }

    public async Task<AppResult<OrganizationTeamDto>> CreateAsync(Guid id, UpsertOrganizationTeamRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationTeamDto>.Validation("Team name is required.");
        }

        OrganizationTeam team = new()
        {
            OrganizationId = id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim()
        };

        await teams.AddAsync(team, cancellationToken);
        await teams.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationTeamDto>.Ok(team.ToDto());
    }

    public async Task<AppResult<OrganizationTeamDto>> UpdateAsync(Guid id, Guid teamId, UpsertOrganizationTeamRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationTeamDto>.Validation("Team name is required.");
        }

        OrganizationTeam? team = await teams.FindWithMembersAsync(id, teamId, cancellationToken);
        if (team is null)
        {
            return AppResult<OrganizationTeamDto>.NotFound();
        }

        team.Name = request.Name.Trim();
        team.Description = request.Description?.Trim();
        await teams.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationTeamDto>.Ok(team.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid id, Guid teamId, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        OrganizationTeam? team = await teams.FindAsync(id, teamId, cancellationToken);
        if (team is null)
        {
            return AppResult.NotFound();
        }

        teams.Remove(team);
        await teams.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult<IReadOnlyList<OrganizationTeamMemberDto>>> ListMembersAsync(Guid id, Guid teamId, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await teams.ExistsAsync(id, teamId, cancellationToken))
        {
            return AppResult<IReadOnlyList<OrganizationTeamMemberDto>>.NotFound();
        }

        IReadOnlyList<OrganizationTeamMember> members = await teams.ListMembersAsync(teamId, cancellationToken);
        return AppResult<IReadOnlyList<OrganizationTeamMemberDto>>.Ok(members.Select(item => item.ToDto()).ToArray());
    }

    public async Task<AppResult<OrganizationTeamMemberDto>> AddMemberAsync(Guid id, Guid teamId, AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await teams.ExistsAsync(id, teamId, cancellationToken))
        {
            return AppResult<OrganizationTeamMemberDto>.NotFound();
        }

        bool userBelongsToOrg = await teams.UserBelongsToOrganizationAsync(id, request.UserId, cancellationToken);
        if (!userBelongsToOrg)
        {
            return AppResult<OrganizationTeamMemberDto>.Validation("User must belong to the organization before joining a team.");
        }

        OrganizationRoleDefinition? role = null;
        if (request.RoleId is not null)
        {
            role = await roles.FindAsync(id, request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationTeamMemberDto>.Validation("Role does not belong to this organization.");
            }
        }

        if (OrganizationRoleRules.RoleRequiresOwner(role))
        {
            await authorizer.RequireOwner(id, cancellationToken);
        }

        OrganizationTeamMember member = new()
        {
            TeamId = teamId,
            UserId = request.UserId,
            RoleId = role?.Id
        };

        await teams.AddMemberAsync(member, cancellationToken);
        await teams.SaveChangesAsync(cancellationToken);
        await teams.LoadMemberReferencesAsync(member, cancellationToken);
        return AppResult<OrganizationTeamMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult> RemoveMemberAsync(Guid id, Guid teamId, Guid memberId, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await teams.ExistsAsync(id, teamId, cancellationToken))
        {
            return AppResult.NotFound();
        }

        OrganizationTeamMember? member = await teams.FindMemberAsync(teamId, memberId, cancellationToken);
        if (member is null)
        {
            return AppResult.NotFound();
        }

        teams.RemoveMember(member);
        await teams.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }
}
