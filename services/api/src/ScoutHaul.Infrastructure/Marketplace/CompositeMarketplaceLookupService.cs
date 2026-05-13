using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed class CompositeMarketplaceLookupService(
    IBarcodeLookupService barcodeLookupService,
    IEnumerable<IMarketplacePricingProvider> providers,
    ILogger<CompositeMarketplaceLookupService> logger) : IMarketplaceLookupService
{
    public async Task<MarketplaceLookupResult> SearchByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        string cleanBarcode = barcode.Trim();
        BarcodeLookupResult? product = await barcodeLookupService.LookupAsync(cleanBarcode, cancellationToken);
        string pricingQuery = BuildPricingQuery(cleanBarcode, product);
        var request = new MarketplacePricingRequest(pricingQuery, cleanBarcode, product);
        ProviderLookup providerLookup = await RunProviders(request, isBarcodeLookup: true, cancellationToken);
        return BuildResult(cleanBarcode, product, providerLookup);
    }

    public async Task<MarketplaceLookupResult> SearchByKeywordAsync(string keyword, CancellationToken cancellationToken = default)
    {
        string query = keyword.Trim();
        var request = new MarketplacePricingRequest(query, Barcode: null, Product: null);
        ProviderLookup providerLookup = await RunProviders(request, isBarcodeLookup: false, cancellationToken);
        return BuildResult(query, product: null, providerLookup);
    }

    private async Task<ProviderLookup> RunProviders(
        MarketplacePricingRequest request,
        bool isBarcodeLookup,
        CancellationToken cancellationToken)
    {
        List<MarketplaceListingDto> listings = [];
        List<string> providersUsed = [];
        List<string> warnings = [];

        foreach (IMarketplacePricingProvider provider in providers.Where(provider => provider.IsConfigured))
        {
            try
            {
                IReadOnlyList<MarketplaceListingDto> providerListings = isBarcodeLookup
                    ? await provider.SearchByBarcodeAsync(request, cancellationToken)
                    : await provider.SearchByKeywordAsync(request, cancellationToken);

                if (providerListings.Count == 0)
                {
                    continue;
                }

                providersUsed.Add(provider.Name);
                listings.AddRange(providerListings);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "{Provider} marketplace lookup failed for {Query}", provider.Name, request.Query);
                warnings.Add($"{provider.Name} lookup failed.");
            }
        }

        return new ProviderLookup(Deduplicate(listings), providersUsed.Distinct().ToArray(), warnings.Distinct().ToArray());
    }

    private static MarketplaceLookupResult BuildResult(string query, BarcodeLookupResult? product, ProviderLookup providerLookup)
    {
        decimal[] prices = providerLookup.Listings
            .Where(listing => listing.Price > 0)
            .Select(listing => listing.Price + (listing.ShippingCost ?? 0))
            .ToArray();

        return new MarketplaceLookupResult(
            Query: query,
            Listings: providerLookup.Listings,
            AverageSoldPrice: prices.Length > 0 ? decimal.Round(prices.Average(), 2, MidpointRounding.AwayFromZero) : null,
            LowestPrice: prices.Length > 0 ? prices.Min() : null,
            HighestPrice: prices.Length > 0 ? prices.Max() : null,
            TotalResults: providerLookup.Listings.Count,
            ProductTitle: product?.Title,
            ProductBrand: product?.Brand,
            ProductImageUrl: product?.ImageUrl,
            ProductCategory: product?.Category,
            ProvidersUsed: providerLookup.ProvidersUsed,
            ProviderWarnings: providerLookup.Warnings);
    }

    private static string BuildPricingQuery(string barcode, BarcodeLookupResult? product)
    {
        string query = string.Join(' ', product?.Brand, product?.Title).Trim();
        return string.IsNullOrWhiteSpace(query) ? barcode : query;
    }

    private static List<MarketplaceListingDto> Deduplicate(IEnumerable<MarketplaceListingDto> listings)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<MarketplaceListingDto> result = [];

        foreach (MarketplaceListingDto listing in listings.Where(listing => listing.Price > 0))
        {
            string key = !string.IsNullOrWhiteSpace(listing.ItemUrl)
                ? $"{listing.Source}|{listing.ItemUrl}"
                : $"{listing.Source}|{listing.Title}|{listing.Condition}|{listing.Price}|{listing.ShippingCost}";

            if (seen.Add(key))
            {
                result.Add(listing);
            }
        }

        return result
            .OrderByDescending(listing => listing.IsSold)
            .ThenBy(listing => listing.Price + (listing.ShippingCost ?? 0))
            .Take(40)
            .ToList();
    }

    private sealed record ProviderLookup(
        IReadOnlyList<MarketplaceListingDto> Listings,
        IReadOnlyList<string> ProvidersUsed,
        IReadOnlyList<string> Warnings);
}
