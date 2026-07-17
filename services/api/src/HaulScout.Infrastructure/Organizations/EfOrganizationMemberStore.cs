using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Organizations;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Organizations;

public sealed class EfOrganizationMemberStore(HaulScoutDbContext db) : IOrganizationMemberStore
{
    public async Task<IReadOnlyList<OrganizationMember>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationMembers
            .Include(member => member.User)
            .Include(member => member.RoleDefinition)
            .Where(member => member.OrganizationId == organizationId)
            .OrderBy(member => member.User!.DisplayName)
            .ToListAsync(cancellationToken);

    public Task<Organization?> FindOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);

    public Task<AppUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(existing => existing.Email == email, cancellationToken);

    public async Task AddUserAsync(AppUser user, CancellationToken cancellationToken) =>
        await db.Users.AddAsync(user, cancellationToken);

    public Task<bool> MemberExistsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.AnyAsync(member => member.OrganizationId == organizationId && member.UserId == userId, cancellationToken);

    public async Task AddMemberAsync(OrganizationMember member, CancellationToken cancellationToken) =>
        await db.OrganizationMembers.AddAsync(member, cancellationToken);

    public Task<OrganizationMember?> FindMemberWithUserAndRoleAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken) =>
        db.OrganizationMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == organizationId, cancellationToken);

    public Task<OrganizationMember?> FindMemberWithRoleAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken) =>
        db.OrganizationMembers
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == organizationId, cancellationToken);

    public Task<int> CountOwnersAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.CountAsync(item =>
            item.OrganizationId == organizationId
            && (item.Role == OrganizationRole.Owner || (item.RoleDefinition != null && item.RoleDefinition.IsOwnerRole)),
            cancellationToken);

    public async Task<IReadOnlyList<OrganizationTeamMember>> ListTeamMembershipsForUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        await db.OrganizationTeamMembers
            .Where(item => item.UserId == userId && item.Team!.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

    public void RemoveTeamMemberships(IEnumerable<OrganizationTeamMember> memberships) =>
        db.OrganizationTeamMembers.RemoveRange(memberships);

    public void Remove(OrganizationMember member) => db.OrganizationMembers.Remove(member);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
