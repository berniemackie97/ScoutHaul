using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Contracts;
using HaulScout.Infrastructure.Marketplace;

namespace HaulScout.Tests.Marketplace;

/// <summary>
/// Tests that eBay barcode search uses GTIN only, and keyword fallback
/// only fires when a RESOLVED product name exists — never raw barcode digits.
/// This was the root cause of "ranch cup → video game" bug.
/// </summary>
public sealed class EbayKeywordFallbackTests
{
    private static IConfiguration BuildConfig(string clientId = "test-id", string clientSecret = "test-secret") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ebay:ClientId"] = clientId,
                ["Ebay:ClientSecret"] = clientSecret,
                ["Ebay:UseSandbox"] = "false",
            })
            .Build();

    /// <summary>
    /// Creates an <see cref="EbayMarketplaceLookupService"/> with a fake HTTP handler
    /// that intercepts requests and returns canned responses.
    /// </summary>
    private static (EbayMarketplaceLookupService Service, FakeEbayHandler Handler) CreateService()
    {
        var handler = new FakeEbayHandler();
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var config = BuildConfig();
        var logger = NullLogger<EbayMarketplaceLookupService>.Instance;
        var service = new EbayMarketplaceLookupService(httpClient, config, logger);
        return (service, handler);
    }

    [Fact]
    public async Task Barcode_search_sends_GTIN_query()
    {
        var (service, handler) = CreateService();
        handler.SetTokenResponse();
        handler.SetSearchResponse([]); // empty — no GTIN match

        var request = new MarketplacePricingRequest("070038643951", "070038643951", null);
        await service.SearchByBarcodeAsync(request, CancellationToken.None);

        // Should have called with gtin= parameter
        Assert.Contains(handler.RequestUrls, url => url.Contains("gtin=070038643951"));
    }

    [Fact]
    public async Task Barcode_search_does_NOT_keyword_fallback_with_raw_barcode()
    {
        var (service, handler) = CreateService();
        handler.SetTokenResponse();
        handler.SetSearchResponse([]); // GTIN returns nothing

        // No product resolved — Product is null
        var request = new MarketplacePricingRequest("070038643951", "070038643951", null);
        await service.SearchByBarcodeAsync(request, CancellationToken.None);

        // Should NOT have made a keyword search with the barcode digits
        Assert.DoesNotContain(handler.RequestUrls, url => url.Contains("q=070038643951"));
        // Only the token request + one GTIN search
        Assert.Equal(2, handler.RequestUrls.Count);
    }

    [Fact]
    public async Task Barcode_search_keyword_fallback_uses_product_name_when_resolved()
    {
        var (service, handler) = CreateService();
        handler.SetTokenResponse();
        handler.SetSearchResponse([]); // GTIN returns nothing, then keyword returns something

        var product = new BarcodeLookupResult(
            "070038643951", "Ranch Cup", "Hidden Valley", null, null, null, "070038643951", null);

        var request = new MarketplacePricingRequest("Hidden Valley Ranch Cup", "070038643951", product);
        await service.SearchByBarcodeAsync(request, CancellationToken.None);

        // Should have keyword fallback with product name, NOT barcode digits
        Assert.Contains(handler.RequestUrls, url =>
            url.Contains("q=") && url.Contains("Hidden") && url.Contains("Ranch"));
        // Should NOT contain raw barcode as keyword
        Assert.DoesNotContain(handler.RequestUrls, url => url.Contains("q=070038643951"));
    }

    [Fact]
    public async Task Barcode_search_no_keyword_fallback_when_product_title_is_empty()
    {
        var (service, handler) = CreateService();
        handler.SetTokenResponse();
        handler.SetSearchResponse([]);

        // Product resolved but with empty title
        var product = new BarcodeLookupResult(
            "070038643951", "", null, null, null, null, "070038643951", null);

        var request = new MarketplacePricingRequest("070038643951", "070038643951", product);
        await service.SearchByBarcodeAsync(request, CancellationToken.None);

        // Only token + GTIN search, no keyword fallback
        Assert.Equal(2, handler.RequestUrls.Count);
    }

    [Fact]
    public void IsConfigured_false_when_no_credentials()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var httpClient = new HttpClient();
        var service = new EbayMarketplaceLookupService(httpClient, config,
            NullLogger<EbayMarketplaceLookupService>.Instance);

        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void IsConfigured_true_when_credentials_present()
    {
        var config = BuildConfig();
        var httpClient = new HttpClient();
        var service = new EbayMarketplaceLookupService(httpClient, config,
            NullLogger<EbayMarketplaceLookupService>.Instance);

        Assert.True(service.IsConfigured);
    }

    // ── Fake HTTP handler ────────────────────────────────────────────

    private sealed class FakeEbayHandler : HttpMessageHandler
    {
        private string _tokenJson = "";
        private string _searchJson = "";
        public List<string> RequestUrls { get; } = [];

        public void SetTokenResponse(string token = "fake-token", int expiresIn = 7200)
        {
            _tokenJson = JsonSerializer.Serialize(new
            {
                access_token = token,
                expires_in = expiresIn,
                token_type = "Application Access Token"
            });
        }

        public void SetSearchResponse(List<object> items)
        {
            _searchJson = JsonSerializer.Serialize(new
            {
                itemSummaries = items,
                total = items.Count
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? "";
            RequestUrls.Add(url);

            string responseJson;
            if (url.Contains("/oauth2/token"))
            {
                responseJson = _tokenJson;
            }
            else
            {
                responseJson = _searchJson;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
