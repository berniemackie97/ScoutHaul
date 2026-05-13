using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed record MarketplacePricingRequest(
    string Query,
    string? Barcode,
    BarcodeLookupResult? Product);

internal interface IMarketplacePricingProvider
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
