using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Organizations;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Organizations;

public sealed class EfOrganizationTeamStore(HaulScoutDbContext db) : IOrganizationTeamStore
{
    public async Task<IReadOnlyList<OrganizationTeam>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationTeams
            .Include(team => team.Members)
            .Where(team => team.OrganizationId == organizationId)
            .OrderBy(team => team.Name)
            .ToListAsync(cancellationToken);

    public Task<OrganizationTeam?> FindAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken) =>
        db.OrganizationTeams.SingleOrDefaultAsync(item => item.Id == teamId && item.OrganizationId == organizationId, cancellationToken);

    public Task<OrganizationTeam?> FindWithMembersAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken) =>
        db.OrganizationTeams
            .Include(item => item.Members)
            .SingleOrDefaultAsync(item => item.Id == teamId && item.OrganizationId == organizationId, cancellationToken);

    public Task<bool> ExistsAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken) =>
        db.OrganizationTeams.AnyAsync(team => team.Id == teamId && team.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<OrganizationTeamMember>> ListMembersAsync(Guid teamId, CancellationToken cancellationToken) =>
        await db.OrganizationTeamMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .Where(item => item.TeamId == teamId)
            .OrderBy(item => item.User!.DisplayName)
            .ToListAsync(cancellationToken);

    public Task<bool> UserBelongsToOrganizationAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.AnyAsync(item => item.OrganizationId == organizationId && item.UserId == userId, cancellationToken);

    public async Task AddAsync(OrganizationTeam team, CancellationToken cancellationToken) =>
        await db.OrganizationTeams.AddAsync(team, cancellationToken);

    public async Task AddMemberAsync(OrganizationTeamMember member, CancellationToken cancellationToken) =>
        await db.OrganizationTeamMembers.AddAsync(member, cancellationToken);

    public Task<OrganizationTeamMember?> FindMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken) =>
        db.OrganizationTeamMembers.SingleOrDefaultAsync(item => item.Id == memberId && item.TeamId == teamId, cancellationToken);

    public async Task LoadMemberReferencesAsync(OrganizationTeamMember member, CancellationToken cancellationToken)
    {
        member.User = await db.Users.FindAsync([member.UserId], cancellationToken);
        if (member.RoleId is not null)
        {
            member.RoleDefinition = await db.OrganizationRoles
                .Include(role => role.Permissions)
                .SingleOrDefaultAsync(role => role.Id == member.RoleId.Value, cancellationToken);
        }
    }

    public void Remove(OrganizationTeam team) => db.OrganizationTeams.Remove(team);

    public void RemoveMember(OrganizationTeamMember member) => db.OrganizationTeamMembers.Remove(member);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
