using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Marketplace;

internal sealed class CompositeMarketplaceLookupService(
    IBarcodeLookupService barcodeLookupService,
    IEnumerable<IMarketplacePricingProvider> providers,
    IResaleEstimator resaleEstimator,
    ILogger<CompositeMarketplaceLookupService> logger) : IMarketplaceLookupService
{
    public async Task<MarketplaceLookupResult> SearchByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        string cleanBarcode = barcode.Trim();
        BarcodeLookupResult? product = await barcodeLookupService.LookupAsync(cleanBarcode, cancellationToken);
        string pricingQuery = BuildPricingQuery(cleanBarcode, product);
        MarketplacePricingRequest request = new(pricingQuery, cleanBarcode, product);
        ProviderLookup providerLookup = await RunProviders(request, isBarcodeLookup: true, cancellationToken);

        // The free UPC providers return retail/reference links (Amazon, Google Shopping), NOT resale
        // sold comps. A reseller needs the RESALE value, so run the grounded estimate whenever we
        // know the product and don't already have real secondhand comps (sold or active). This is
        // what gives barcode scans the same price + comps the photo flow shows.
        ResaleEstimate? estimate = null;
        bool hasSecondhandComps = providerLookup.Listings.Any(IsSecondhand);
        if (!hasSecondhandComps && !string.IsNullOrWhiteSpace(product?.Title))
        {
            string description = string.Join(' ', product.Brand, product.Title, product.Category).Trim();
            estimate = await TryEstimateAsync(description, cancellationToken);
        }

        return BuildResult(cleanBarcode, product, providerLookup, estimate);
    }

    public async Task<MarketplaceLookupResult> SearchByKeywordAsync(string keyword, CancellationToken cancellationToken = default)
    {
        string query = keyword.Trim();
        MarketplacePricingRequest request = new(query, Barcode: null, Product: null);
        ProviderLookup providerLookup = await RunProviders(request, isBarcodeLookup: false, cancellationToken);

        // Estimate unless we already have real secondhand comps. Retail/reference listings don't
        // count — the reseller still needs a resale value. This also powers the photo flow, which
        // calls keyword search to upgrade its vision estimate to grounded sold/active comps.
        bool hasSecondhandComps = providerLookup.Listings.Any(IsSecondhand);
        ResaleEstimate? estimate = !hasSecondhandComps
            ? await TryEstimateAsync(query, cancellationToken)
            : null;

        return BuildResult(query, product: null, providerLookup, estimate);
    }

    private async Task<ResaleEstimate?> TryEstimateAsync(string description, CancellationToken cancellationToken)
    {
        if (!resaleEstimator.IsConfigured || string.IsNullOrWhiteSpace(description))
        {
            return null;
        }
        try
        {
            ResaleSearchHints searchHints = ResaleSearchHintBuilder.FromQuery(description);
            return await resaleEstimator.EstimateAsync(description, searchHints, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resale estimate failed for {Description}", description);
            return null;
        }
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

    private static MarketplaceLookupResult BuildResult(
        string query, BarcodeLookupResult? product, ProviderLookup providerLookup, ResaleEstimate? estimate)
    {
        // "Sold price" must reflect REAL resale signal only (sold or active secondhand). Retail /
        // reference listings are shown for context but excluded here, so they can't masquerade as a
        // sold comp or suppress the grounded resale estimate on the client.
        decimal[] prices = providerLookup.Listings
            .Where(listing => IsSecondhand(listing) && listing.Price > 0)
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
            ProviderWarnings: providerLookup.Warnings,
            EstimatedResaleLow: estimate?.Low,
            EstimatedResaleHigh: estimate?.High,
            EstimateSource: estimate is not null ? "ai" : null,
            EstimateRationale: estimate?.Rationale,
            EstimatePriceRangeType: estimate?.PriceRangeType,
            EstimateValuationConfidence: estimate?.ValuationConfidence,
            EstimateComps: estimate?.Comps,
            EstimateSources: estimate?.Sources);
    }

    // Real resale signal: a realized sale, or an active secondhand listing. Retail/reference is not.
    private static bool IsSecondhand(MarketplaceListingDto listing) =>
        listing.IsSold
        || listing.MarketTier == MarketTiers.SoldSecondhand
        || listing.MarketTier == MarketTiers.ActiveSecondhand;

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
            // Resale priority: sold-secondhand → active-secondhand → retail, then most recent,
            // then cheapest total. Gives the reseller realized comps first, freshest first.
            .OrderBy(listing => MarketTiers.Rank(listing.MarketTier))
            .ThenByDescending(listing => listing.SoldDate ?? listing.ListedDate ?? DateTimeOffset.MinValue)
            .ThenByDescending(listing => listing.IsSold)
            .ThenBy(listing => listing.Price + (listing.ShippingCost ?? 0))
            .Take(40)
            .ToList();
    }

    private sealed record ProviderLookup(
        IReadOnlyList<MarketplaceListingDto> Listings,
        IReadOnlyList<string> ProvidersUsed,
        IReadOnlyList<string> Warnings);
}
