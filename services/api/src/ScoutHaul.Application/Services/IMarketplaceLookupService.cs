using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Application.Services;

public interface IMarketplaceLookupService
{
    Task<MarketplaceLookupResult> SearchByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task<MarketplaceLookupResult> SearchByKeywordAsync(string keyword, CancellationToken cancellationToken = default);
}
