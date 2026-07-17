using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sessions;

public interface ISessionBootstrapStore
{
    Task<AppUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationDto>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken);

    Task<SessionRemoteCountsDto> GetRemoteCountsAsync(Guid? organizationId, CancellationToken cancellationToken);
}
