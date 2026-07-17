using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Organizations;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Organizations;

public sealed class EfOrganizationInviteCodeStore(HaulScoutDbContext db) : IOrganizationInviteCodeStore
{
    public async Task<IReadOnlyList<OrganizationInviteCode>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationInviteCodes
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .Where(item => item.OrganizationId == organizationId)
            .OrderByDescending(item => item.IsActive)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.AnyAsync(item => item.Id == organizationId, cancellationToken);

    public Task<OrganizationTeam?> FindTeamAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken) =>
        db.OrganizationTeams.SingleOrDefaultAsync(
            item => item.Id == teamId && item.OrganizationId == organizationId,
            cancellationToken);

    public Task<bool> CodeHashExistsAsync(string codeHash, CancellationToken cancellationToken) =>
        db.OrganizationInviteCodes.AnyAsync(item => item.CodeHash == codeHash, cancellationToken);

    public async Task AddAsync(OrganizationInviteCode inviteCode, CancellationToken cancellationToken) =>
        await db.OrganizationInviteCodes.AddAsync(inviteCode, cancellationToken);

    public Task<OrganizationInviteCode?> FindAsync(Guid organizationId, Guid inviteCodeId, CancellationToken cancellationToken) =>
        db.OrganizationInviteCodes
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == inviteCodeId && item.OrganizationId == organizationId, cancellationToken);

    public Task<OrganizationInviteCode?> FindByCodeHashAsync(string codeHash, CancellationToken cancellationToken) =>
        db.OrganizationInviteCodes
            .Include(item => item.Organization)
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.CodeHash == codeHash, cancellationToken);

    public async Task<AppUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.FindAsync([userId], cancellationToken);

    public Task<OrganizationMember?> FindMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.UserId == userId, cancellationToken);

    public Task<bool> TeamMembershipExistsAsync(Guid teamId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationTeamMembers.AnyAsync(item => item.TeamId == teamId && item.UserId == userId, cancellationToken);

    public async Task AddMemberAsync(OrganizationMember member, CancellationToken cancellationToken) =>
        await db.OrganizationMembers.AddAsync(member, cancellationToken);

    public async Task AddTeamMemberAsync(OrganizationTeamMember member, CancellationToken cancellationToken) =>
        await db.OrganizationTeamMembers.AddAsync(member, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
