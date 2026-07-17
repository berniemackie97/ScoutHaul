using HaulScout.Application.Contracts;

namespace HaulScout.Application.Sales;

public interface ISalesChannelStore
{
    Task<IReadOnlyList<string>> ListExistingChannelsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SaleCandidateDto>> ListSaleCandidatesAsync(CancellationToken cancellationToken);
}
