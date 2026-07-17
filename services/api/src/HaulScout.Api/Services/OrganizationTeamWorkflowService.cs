using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Teams and their membership. A team is a grouping inside one organization, so you must already belong to the
/// organization to join one — and a team role that would grant owner-level power still needs an owner.
/// </summary>
public sealed class OrganizationTeamWorkflowService(
    HaulScoutDbContext db,
    OrganizationAccessService access,
    OrganizationRoleGuard guard)
{
    public async Task<IReadOnlyList<OrganizationTeamDto>> ListAsync(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        List<OrganizationTeam> teams = await db.OrganizationTeams
            .Include(team => team.Members)
            .Where(team => team.OrganizationId == id)
            .OrderBy(team => team.Name)
            .ToListAsync(cancellationToken);

        return teams.Select(team => team.ToDto()).ToArray();
    }

    public async Task<AppResult<OrganizationTeamDto>> CreateAsync(Guid id, UpsertOrganizationTeamRequest request, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
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

        db.OrganizationTeams.Add(team);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationTeamDto>.Ok(team.ToDto());
    }

    public async Task<AppResult<OrganizationTeamDto>> UpdateAsync(Guid id, Guid teamId, UpsertOrganizationTeamRequest request, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationTeamDto>.Validation("Team name is required.");
        }

        OrganizationTeam? team = await db.OrganizationTeams
            .Include(item => item.Members)
            .SingleOrDefaultAsync(item => item.Id == teamId && item.OrganizationId == id, cancellationToken);
        if (team is null)
        {
            return AppResult<OrganizationTeamDto>.NotFound();
        }

        team.Name = request.Name.Trim();
        team.Description = request.Description?.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationTeamDto>.Ok(team.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid id, Guid teamId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        OrganizationTeam? team = await db.OrganizationTeams
            .SingleOrDefaultAsync(item => item.Id == teamId && item.OrganizationId == id, cancellationToken);
        if (team is null)
        {
            return AppResult.NotFound();
        }

        db.OrganizationTeams.Remove(team);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult<IReadOnlyList<OrganizationTeamMemberDto>>> ListMembersAsync(Guid id, Guid teamId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await guard.TeamBelongsToOrganizationAsync(id, teamId, cancellationToken))
        {
            return AppResult<IReadOnlyList<OrganizationTeamMemberDto>>.NotFound();
        }

        List<OrganizationTeamMember> members = await db.OrganizationTeamMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .Where(item => item.TeamId == teamId)
            .OrderBy(item => item.User!.DisplayName)
            .ToListAsync(cancellationToken);

        return AppResult<IReadOnlyList<OrganizationTeamMemberDto>>.Ok(members.Select(item => item.ToDto()).ToArray());
    }

    public async Task<AppResult<OrganizationTeamMemberDto>> AddMemberAsync(Guid id, Guid teamId, AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await guard.TeamBelongsToOrganizationAsync(id, teamId, cancellationToken))
        {
            return AppResult<OrganizationTeamMemberDto>.NotFound();
        }

        bool userBelongsToOrg = await db.OrganizationMembers.AnyAsync(
            item => item.OrganizationId == id && item.UserId == request.UserId, cancellationToken);
        if (!userBelongsToOrg)
        {
            return AppResult<OrganizationTeamMemberDto>.Validation("User must belong to the organization before joining a team.");
        }

        OrganizationRoleDefinition? role = null;
        if (request.RoleId is not null)
        {
            role = await guard.LoadRoleAsync(id, request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationTeamMemberDto>.Validation("Role does not belong to this organization.");
            }
        }

        if (OrganizationRoleGuard.RoleRequiresOwner(role))
        {
            await access.RequireOwner(id, cancellationToken);
        }

        OrganizationTeamMember member = new()
        {
            TeamId = teamId,
            UserId = request.UserId,
            RoleId = role?.Id
        };

        db.OrganizationTeamMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        member.User = await db.Users.FindAsync([request.UserId], cancellationToken);
        member.RoleDefinition = role;
        return AppResult<OrganizationTeamMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult> RemoveMemberAsync(Guid id, Guid teamId, Guid memberId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await guard.TeamBelongsToOrganizationAsync(id, teamId, cancellationToken))
        {
            return AppResult.NotFound();
        }

        OrganizationTeamMember? member = await db.OrganizationTeamMembers
            .SingleOrDefaultAsync(item => item.Id == memberId && item.TeamId == teamId, cancellationToken);
        if (member is null)
        {
            return AppResult.NotFound();
        }

        db.OrganizationTeamMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }
}
