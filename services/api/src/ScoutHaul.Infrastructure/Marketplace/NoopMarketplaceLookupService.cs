using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Marketplace;

public sealed class NoopMarketplaceLookupService : IMarketplaceLookupService
{
    public Task<MarketplaceLookupResult> SearchByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        => Task.FromResult(new MarketplaceLookupResult(barcode, [], null, null, null, 0));

    public Task<MarketplaceLookupResult> SearchByKeywordAsync(string keyword, CancellationToken cancellationToken = default)
        => Task.FromResult(new MarketplaceLookupResult(keyword, [], null, null, null, 0));
}
