using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IMarketplaceLookupService
{
    Task<MarketplaceLookupResult> SearchByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task<MarketplaceLookupResult> SearchByKeywordAsync(string keyword, CancellationToken cancellationToken = default);
}
