using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Infrastructure.Marketplace;

namespace HaulScout.Tests.Marketplace;

public sealed class CompositeMarketplaceLookupTests
{
    // ── Stubs ────────────────────────────────────────────────────────

    private sealed class StubBarcodeLookupService(BarcodeLookupResult? result) : IBarcodeLookupService
    {
        public int CallCount { get; private set; }
        public string? LastBarcode { get; private set; }

        public Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastBarcode = barcode;
            return Task.FromResult(result);
        }
    }

    private sealed class StubPricingProvider(
        string name,
        bool isConfigured,
        List<MarketplaceListingDto>? barcodeListings = null,
        List<MarketplaceListingDto>? keywordListings = null) : IMarketplacePricingProvider
    {
        public string Name => name;
        public bool IsConfigured => isConfigured;
        public int BarcodeCallCount { get; private set; }
        public int KeywordCallCount { get; private set; }
        public MarketplacePricingRequest? LastBarcodeRequest { get; private set; }

        public Task<IReadOnlyList<MarketplaceListingDto>> SearchByBarcodeAsync(
            MarketplacePricingRequest request, CancellationToken cancellationToken)
        {
            BarcodeCallCount++;
            LastBarcodeRequest = request;
            return Task.FromResult<IReadOnlyList<MarketplaceListingDto>>(barcodeListings ?? []);
        }

        public Task<IReadOnlyList<MarketplaceListingDto>> SearchByKeywordAsync(
            MarketplacePricingRequest request, CancellationToken cancellationToken)
        {
            KeywordCallCount++;
            return Task.FromResult<IReadOnlyList<MarketplaceListingDto>>(keywordListings ?? []);
        }
    }

    private sealed class ThrowingPricingProvider(string name) : IMarketplacePricingProvider
    {
        public string Name => name;
        public bool IsConfigured => true;

        public Task<IReadOnlyList<MarketplaceListingDto>> SearchByBarcodeAsync(
            MarketplacePricingRequest request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated failure");

        public Task<IReadOnlyList<MarketplaceListingDto>> SearchByKeywordAsync(
            MarketplacePricingRequest request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated failure");
    }

    // Default: estimator disabled, so existing pricing assertions are unaffected.
    private sealed class NoEstimator : IResaleEstimator
    {
        public bool IsConfigured => false;
        public Task<ResaleEstimate?> EstimateAsync(string d, ResaleSearchHints? searchHints, CancellationToken ct) => Task.FromResult<ResaleEstimate?>(null);
    }

    private sealed class StubEstimator(ResaleEstimate? estimate) : IResaleEstimator
    {
        public bool IsConfigured => true;
        public int CallCount { get; private set; }
        public Task<ResaleEstimate?> EstimateAsync(string d, ResaleSearchHints? searchHints, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(estimate);
        }
    }

    // Generic comp = a "Used" eBay listing → active-secondhand tier (real resale signal).
    private static MarketplaceListingDto MakeListing(
        string title, decimal price, string source = "eBay", string? itemUrl = null) =>
        new(title, price, null, null, itemUrl ?? $"https://example.com/{title}", "Used", source, false, null,
            MarketTiers.ActiveSecondhand);

    private static BarcodeLookupResult MakeProduct(string barcode, string title, string? brand = null) =>
        new(barcode, title, brand, null, null, null, barcode, null);

    // ── SearchByBarcodeAsync tests ───────────────────────────────────

    [Fact]
    public async Task Barcode_search_calls_barcode_lookup_service_first()
    {
        StubBarcodeLookupService barcodeLookup = new(MakeProduct("123", "Ranch Cup", "Hidden Valley"));
        StubPricingProvider provider = new("eBay", true);
        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        await sut.SearchByBarcodeAsync("123");

        Assert.Equal(1, barcodeLookup.CallCount);
        Assert.Equal("123", barcodeLookup.LastBarcode);
    }

    [Fact]
    public async Task Barcode_search_uses_product_name_as_pricing_query_when_resolved()
    {
        StubBarcodeLookupService barcodeLookup = new(MakeProduct("123", "Ranch Cup", "Hidden Valley"));
        StubPricingProvider provider = new("eBay", true);
        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        await sut.SearchByBarcodeAsync("123");

        // Query should be "Hidden Valley Ranch Cup", NOT the raw barcode
        Assert.NotNull(provider.LastBarcodeRequest);
        Assert.Equal("Hidden Valley Ranch Cup", provider.LastBarcodeRequest.Query);
        Assert.Equal("123", provider.LastBarcodeRequest.Barcode);
    }

    [Fact]
    public async Task Barcode_search_uses_raw_barcode_as_query_when_product_not_resolved()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubPricingProvider provider = new("eBay", true);
        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        await sut.SearchByBarcodeAsync("070038643951");

        Assert.NotNull(provider.LastBarcodeRequest);
        Assert.Equal("070038643951", provider.LastBarcodeRequest.Query);
        Assert.Null(provider.LastBarcodeRequest.Product);
    }

    [Fact]
    public async Task Barcode_search_populates_product_metadata_in_result()
    {
        StubBarcodeLookupService barcodeLookup = new(
            MakeProduct("123", "Ranch Cup", "Hidden Valley"));
        CompositeMarketplaceLookupService sut = new(barcodeLookup, [],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal("Ranch Cup", result.ProductTitle);
        Assert.Equal("Hidden Valley", result.ProductBrand);
    }

    [Fact]
    public async Task Barcode_search_aggregates_listings_from_multiple_providers()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubPricingProvider ebay = new("eBay", true,
            barcodeListings: [MakeListing("eBay Item", 25.00m, "eBay")]);
        StubPricingProvider amazon = new("Amazon", true,
            barcodeListings: [MakeListing("Amazon Item", 30.00m, "Amazon")]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [ebay, amazon],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(2, result.TotalResults);
        Assert.Contains(result.ProvidersUsed!, p => p == "eBay");
        Assert.Contains(result.ProvidersUsed!, p => p == "Amazon");
    }

    [Fact]
    public async Task Barcode_search_calculates_price_stats_correctly()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubPricingProvider provider = new("eBay", true,
            barcodeListings:
            [
                MakeListing("Item A", 10.00m),
                MakeListing("Item B", 20.00m),
                MakeListing("Item C", 30.00m),
            ]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(10.00m, result.LowestPrice);
        Assert.Equal(30.00m, result.HighestPrice);
        Assert.Equal(20.00m, result.AverageSoldPrice); // (10+20+30)/3
    }

    [Fact]
    public async Task Barcode_search_returns_null_stats_when_no_listings()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        CompositeMarketplaceLookupService sut = new(barcodeLookup, [],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Null(result.AverageSoldPrice);
        Assert.Null(result.LowestPrice);
        Assert.Null(result.HighestPrice);
        Assert.Equal(0, result.TotalResults);
    }

    [Fact]
    public async Task Barcode_search_survives_provider_exception()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        ThrowingPricingProvider broken = new("Broken");
        StubPricingProvider good = new("Good", true,
            barcodeListings: [MakeListing("Survived", 15.00m)]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [broken, good],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(1, result.TotalResults);
        Assert.Contains(result.ProviderWarnings!, w => w.Contains("Broken"));
    }

    [Fact]
    public async Task Barcode_search_skips_unconfigured_providers()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubPricingProvider unconfigured = new("Unconfigured", false,
            barcodeListings: [MakeListing("Should Not Appear", 99.99m)]);
        StubPricingProvider configured = new("Configured", true,
            barcodeListings: [MakeListing("Should Appear", 10.00m)]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [unconfigured, configured],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(1, result.TotalResults);
        Assert.Equal(0, unconfigured.BarcodeCallCount);
    }

    [Fact]
    public async Task Barcode_search_deduplicates_by_url()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubPricingProvider p1 = new("P1", true,
            barcodeListings: [MakeListing("Same Item", 20.00m, "eBay", "https://ebay.com/item/123")]);
        StubPricingProvider p2 = new("P2", true,
            barcodeListings: [MakeListing("Same Item", 20.00m, "eBay", "https://ebay.com/item/123")]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [p1, p2],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(1, result.TotalResults); // deduped
    }

    // ── SearchByKeywordAsync tests ───────────────────────────────────

    [Fact]
    public async Task Keyword_search_does_not_call_barcode_lookup()
    {
        StubBarcodeLookupService barcodeLookup = new(MakeProduct("123", "Should Not Resolve"));
        StubPricingProvider provider = new("eBay", true,
            keywordListings: [MakeListing("Keyword Result", 12.00m)]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByKeywordAsync("ranch dressing cup");

        Assert.Equal(0, barcodeLookup.CallCount);
        Assert.Equal(1, result.TotalResults);
        Assert.Null(result.ProductTitle); // no product resolution for keyword search
    }

    [Fact]
    public async Task Keyword_search_uses_keyword_method_on_providers()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubPricingProvider provider = new("eBay", true,
            keywordListings: [MakeListing("Keyword Hit", 10.00m)]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        await sut.SearchByKeywordAsync("test query");

        Assert.Equal(1, provider.KeywordCallCount);
        Assert.Equal(0, provider.BarcodeCallCount);
    }

    // ── Price stats edge cases ───────────────────────────────────────

    [Fact]
    public async Task Price_stats_include_shipping_cost()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        // Item: $10 + $5 shipping = $15 total
        MarketplaceListingDto listing = new("Item", 10.00m, 5.00m, null, "https://x.com/1", "New", "eBay", false, null, MarketTiers.ActiveSecondhand);
        StubPricingProvider provider = new("eBay", true,
            barcodeListings: [listing]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(15.00m, result.AverageSoldPrice); // price + shipping
    }

    [Fact]
    public async Task Zero_price_listings_are_excluded()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubPricingProvider provider = new("eBay", true,
            barcodeListings:
            [
                new MarketplaceListingDto("Free", 0m, null, null, "https://x.com/free", "New", "eBay", false, null),
                MakeListing("Paid", 25.00m),
            ]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(),
            NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(1, result.TotalResults); // zero-price excluded
        Assert.Equal(25.00m, result.AverageSoldPrice);
    }

    // ── AI resale estimate fallback ──────────────────────────────────

    [Fact]
    public async Task Barcode_with_no_comps_uses_ai_estimate_when_product_resolved()
    {
        StubBarcodeLookupService barcodeLookup = new(MakeProduct("123", "Ranch Cup", "Hidden Valley"));
        StubEstimator estimator = new(new ResaleEstimate(8m, 15m, "Common kitchen item"));

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [], // no pricing providers
            estimator, NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(1, estimator.CallCount);
        Assert.Equal(8m, result.EstimatedResaleLow);
        Assert.Equal(15m, result.EstimatedResaleHigh);
        Assert.Equal("ai", result.EstimateSource);
        Assert.Equal("Common kitchen item", result.EstimateRationale);
        Assert.Null(result.AverageSoldPrice); // estimate does not masquerade as market data
    }

    [Fact]
    public async Task Barcode_with_comps_does_not_call_estimator()
    {
        StubBarcodeLookupService barcodeLookup = new(MakeProduct("123", "Ranch Cup"));
        StubEstimator estimator = new(new ResaleEstimate(8m, 15m, null));
        StubPricingProvider provider = new("eBay", true, barcodeListings: [MakeListing("Cup", 12m)]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            estimator, NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(0, estimator.CallCount); // market data present → no estimate
        Assert.Null(result.EstimateSource);
        Assert.Equal(12m, result.AverageSoldPrice);
    }

    [Fact]
    public async Task Barcode_with_no_product_does_not_estimate()
    {
        StubBarcodeLookupService barcodeLookup = new(null); // unresolved barcode
        StubEstimator estimator = new(new ResaleEstimate(8m, 15m, null));

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [],
            estimator, NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("999999999");

        Assert.Equal(0, estimator.CallCount); // nothing to estimate from a bare barcode
        Assert.Null(result.EstimateSource);
    }

    [Fact]
    public async Task Retail_only_listings_still_estimate_and_do_not_count_as_sold_price()
    {
        // Free UPC providers return RETAIL reference links (Amazon / Google Shopping), never resale
        // comps. Those must NOT block the grounded resale estimate, nor masquerade as a sold price.
        StubBarcodeLookupService barcodeLookup = new(MakeProduct("123", "Sony WH-1000XM4", "Sony"));
        StubEstimator estimator = new(new ResaleEstimate(120m, 180m, "Used resale"));
        MarketplaceListingDto retail = new("New at Amazon", 349m, null, null, "https://x/retail", "New", "Amazon", false, null, MarketTiers.Retail);
        StubPricingProvider provider = new("Amazon", true, barcodeListings: [retail]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            estimator, NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(1, estimator.CallCount);      // retail did NOT block the resale estimate
        Assert.Equal(120m, result.EstimatedResaleLow);
        Assert.Null(result.AverageSoldPrice);      // retail is NOT a sold comp
        Assert.Single(result.Listings);            // retail still shown for reference
    }

    [Fact]
    public async Task Listings_ranked_sold_then_active_then_retail()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        // Intentionally out of order: retail (cheap), active, sold.
        MarketplaceListingDto retail = new("Retail", 50m, null, null, "https://x/retail", "New", "Google Shopping", false, null, MarketTiers.Retail);
        MarketplaceListingDto active = new("Active", 80m, null, null, "https://x/active", "Used", "eBay", false, null, MarketTiers.ActiveSecondhand);
        MarketplaceListingDto sold = new("Sold", 70m, null, null, "https://x/sold", "Used", "eBay", true, DateTimeOffset.UtcNow, MarketTiers.SoldSecondhand);
        StubPricingProvider provider = new("Mixed", true, barcodeListings: [retail, active, sold]);

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [provider],
            new NoEstimator(), NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByBarcodeAsync("123");

        Assert.Equal(3, result.TotalResults);
        Assert.Equal("Sold", result.Listings[0].Title);   // sold-secondhand first
        Assert.Equal("Active", result.Listings[1].Title);  // then active-secondhand
        Assert.Equal("Retail", result.Listings[2].Title);  // retail last
    }

    [Fact]
    public async Task Keyword_with_no_comps_estimates_from_query()
    {
        StubBarcodeLookupService barcodeLookup = new(null);
        StubEstimator estimator = new(new ResaleEstimate(40m, 90m, "Used furniture"));

        CompositeMarketplaceLookupService sut = new(barcodeLookup, [],
            estimator, NullLogger<CompositeMarketplaceLookupService>.Instance);

        MarketplaceLookupResult result = await sut.SearchByKeywordAsync("wooden rocking chair");

        Assert.Equal(1, estimator.CallCount);
        Assert.Equal(40m, result.EstimatedResaleLow);
        Assert.Equal("ai", result.EstimateSource);
    }
}
