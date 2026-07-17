using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public interface IOrganizationTeamStore
{
    Task<IReadOnlyList<OrganizationTeam>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<OrganizationTeam?> FindAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken);

    Task<OrganizationTeam?> FindWithMembersAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationTeamMember>> ListMembersAsync(Guid teamId, CancellationToken cancellationToken);

    Task<bool> UserBelongsToOrganizationAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task AddAsync(OrganizationTeam team, CancellationToken cancellationToken);

    Task AddMemberAsync(OrganizationTeamMember member, CancellationToken cancellationToken);

    Task<OrganizationTeamMember?> FindMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken);

    Task LoadMemberReferencesAsync(OrganizationTeamMember member, CancellationToken cancellationToken);

    void Remove(OrganizationTeam team);

    void RemoveMember(OrganizationTeamMember member);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
