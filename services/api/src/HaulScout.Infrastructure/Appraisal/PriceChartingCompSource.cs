using HaulScout.Application.Appraisal;
using HaulScout.Application.Contracts;
using HaulScout.Infrastructure.Marketplace;

namespace HaulScout.Infrastructure.Appraisal;

/// <summary>PriceCharting's per-condition guide prices for games and collectibles.</summary>
internal sealed class PriceChartingCompSource(PriceChartingMarketplaceProvider provider) : ICompSource
{
    public string Name => provider.Name;
    public bool IsConfigured => provider.IsConfigured;
    public TimeSpan Deadline => TimeSpan.FromSeconds(10);

    public async Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken)
    {
        BarcodeLookupResult product = new(
            query.Barcode ?? string.Empty, query.Item.Title, query.Item.Brand, null, query.Item.Category, null, null, null);
        MarketplacePricingRequest request = new(query.SearchText, query.Barcode, product);

        IReadOnlyList<MarketplaceListingDto> prices = query.Barcode is null
            ? await provider.SearchByKeywordAsync(request, cancellationToken)
            : await provider.SearchByBarcodeAsync(request, cancellationToken);

        return new CompSourceResult(prices
            .Select(price => new Comp(
                CompKind.Guide, Name, price.Title, price.Price, null, price.Condition, null, null, Verified: true))
            .ToArray());
    }
}
