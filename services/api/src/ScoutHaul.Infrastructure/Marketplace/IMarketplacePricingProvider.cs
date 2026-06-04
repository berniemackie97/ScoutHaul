using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

public sealed record MarketplacePricingRequest(
    string Query,
    string? Barcode,
    BarcodeLookupResult? Product);

public interface IMarketplacePricingProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<MarketplaceListingDto>> SearchByBarcodeAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MarketplaceListingDto>> SearchByKeywordAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken);
}
