using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public interface IOrganizationInviteCodeStore
{
    Task<IReadOnlyList<OrganizationInviteCode>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<OrganizationTeam?> FindTeamAsync(Guid organizationId, Guid teamId, CancellationToken cancellationToken);

    Task<bool> CodeHashExistsAsync(string codeHash, CancellationToken cancellationToken);

    Task AddAsync(OrganizationInviteCode inviteCode, CancellationToken cancellationToken);

    Task<OrganizationInviteCode?> FindAsync(Guid organizationId, Guid inviteCodeId, CancellationToken cancellationToken);

    Task<OrganizationInviteCode?> FindByCodeHashAsync(string codeHash, CancellationToken cancellationToken);

    Task<AppUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<OrganizationMember?> FindMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<bool> TeamMembershipExistsAsync(Guid teamId, Guid userId, CancellationToken cancellationToken);

    Task AddMemberAsync(OrganizationMember member, CancellationToken cancellationToken);

    Task AddTeamMemberAsync(OrganizationTeamMember member, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
