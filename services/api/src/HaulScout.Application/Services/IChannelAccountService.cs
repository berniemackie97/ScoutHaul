using HaulScout.Application.Common;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IChannelAccountService
{
    Task<IReadOnlyList<ChannelAccountDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<AppResult<ChannelAccountDto>> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
    Task<AppResult<ChannelAccountDto>> CreateAsync(Guid organizationId, UpsertChannelAccountRequest request, CancellationToken cancellationToken);
    Task<AppResult<ChannelAccountDto>> UpdateAsync(Guid organizationId, Guid id, UpsertChannelAccountRequest request, CancellationToken cancellationToken);
    Task<AppResult> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
}
