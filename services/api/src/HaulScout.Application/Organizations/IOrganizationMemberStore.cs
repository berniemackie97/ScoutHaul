using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public interface IOrganizationMemberStore
{
    Task<IReadOnlyList<OrganizationMember>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<Organization?> FindOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<AppUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken);

    Task AddUserAsync(AppUser user, CancellationToken cancellationToken);

    Task<bool> MemberExistsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task AddMemberAsync(OrganizationMember member, CancellationToken cancellationToken);

    Task<OrganizationMember?> FindMemberWithUserAndRoleAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken);

    Task<OrganizationMember?> FindMemberWithRoleAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken);

    Task<int> CountOwnersAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationTeamMember>> ListTeamMembershipsForUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    void RemoveTeamMemberships(IEnumerable<OrganizationTeamMember> memberships);

    void Remove(OrganizationMember member);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
