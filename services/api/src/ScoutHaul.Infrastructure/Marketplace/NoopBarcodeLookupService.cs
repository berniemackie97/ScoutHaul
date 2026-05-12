using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Marketplace;

public sealed class NoopBarcodeLookupService : IBarcodeLookupService
{
    public Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
        Task.FromResult<BarcodeLookupResult?>(null);
}
