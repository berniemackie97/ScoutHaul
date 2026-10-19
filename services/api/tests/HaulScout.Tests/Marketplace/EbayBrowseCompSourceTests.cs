using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Appraisal;
using HaulScout.Infrastructure.Appraisal;

namespace HaulScout.Tests.Marketplace;

public sealed class EbayBrowseCompSourceTests
{
    [Fact]
    public async Task A_barcode_is_searched_as_a_gtin_and_listings_come_back_as_asking_prices()
    {
        FakeEbay ebay = new(gtinResults: [Listing("Hidden Valley Ranch Cup 12 pack", "4.99")]);

        CompSourceResult result = await Source(ebay).FindAsync(Query("070038643951"), CancellationToken.None);

        Assert.Contains(ebay.Urls, url => url.Contains("gtin=070038643951", StringComparison.Ordinal));
        Comp comp = Assert.Single(result.Comps);
        Assert.Equal(CompKind.Asking, comp.Kind);
        Assert.Equal(4.99m, comp.Price);
    }

    [Fact]
    public async Task An_empty_barcode_search_falls_back_to_the_name_never_the_digits()
    {
        FakeEbay ebay = new(gtinResults: [], keywordResults: [Listing("Hidden Valley Ranch Cup", "5.49")]);

        await Source(ebay).FindAsync(Query("070038643951"), CancellationToken.None);

        Assert.Contains(ebay.Urls, url => url.Contains("q=Hidden%20Valley%20Ranch%20Cup", StringComparison.Ordinal));
        Assert.DoesNotContain(ebay.Urls, url => url.Contains("q=070038643951", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_fixed_price_listings_in_the_item_s_condition_are_asked_for_and_the_token_is_reused()
    {
        FakeEbay ebay = new(keywordResults: []);
        EbayBrowseCompSource source = Source(ebay, new MemoryCache(new MemoryCacheOptions()));
        CompQuery used = Query(null) with { Item = Query(null).Item with { Condition = "Pre-owned" } };

        await source.FindAsync(used, CancellationToken.None);
        await source.FindAsync(used, CancellationToken.None);

        Assert.Contains(ebay.Urls, url => url.Contains(Uri.EscapeDataString("buyingOptions:{FIXED_PRICE},conditions:{USED}"), StringComparison.Ordinal));
        Assert.Single(ebay.Urls, url => url.Contains("/oauth2/token", StringComparison.Ordinal));
    }

    [Fact]
    public void It_is_off_without_developer_keys()
    {
        EbayBrowseCompSource source = new(new HttpClient(), new ConfigurationBuilder().Build(),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<EbayBrowseCompSource>.Instance);

        Assert.False(source.IsConfigured);
    }

    private static CompQuery Query(string? barcode) => new(
        Guid.NewGuid(),
        new ItemFacts("Hidden Valley Ranch Cup", "Hidden Valley", null, null, null, null, [], IdentificationCertainty.High),
        "Hidden Valley Ranch Cup",
        barcode,
        null);

    private static object Listing(string title, string price) => new
    {
        title,
        price = new { value = price, currency = "USD" },
        condition = "New",
        itemWebUrl = $"https://www.ebay.com/itm/{title.GetHashCode():X}",
    };

    private static EbayBrowseCompSource Source(FakeEbay ebay, IMemoryCache? cache = null) => new(
        new HttpClient(ebay),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ebay:ClientId"] = "id",
            ["Ebay:ClientSecret"] = "secret",
        }).Build(),
        cache ?? new MemoryCache(new MemoryCacheOptions()),
        NullLogger<EbayBrowseCompSource>.Instance);

    private sealed class FakeEbay(List<object>? gtinResults = null, List<object>? keywordResults = null) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Urls.Add(url);
            object body = url.Contains("/oauth2/token", StringComparison.Ordinal)
                ? new { access_token = "token", expires_in = 7200 }
                : new { itemSummaries = url.Contains("gtin=", StringComparison.Ordinal) ? gtinResults ?? [] : keywordResults ?? [] };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            });
        }
    }
}
