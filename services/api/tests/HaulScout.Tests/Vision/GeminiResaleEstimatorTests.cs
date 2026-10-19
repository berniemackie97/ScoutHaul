using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Infrastructure.Vision;

namespace HaulScout.Tests.Vision;

public sealed class GeminiResaleEstimatorTests
{
    private sealed class CountingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastRequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string Envelope(string innerJson)
    {
        string escaped = System.Text.Json.JsonSerializer.Serialize(innerJson);
        return $$"""{ "candidates": [ { "content": { "parts": [ { "text": {{escaped}} } ] } } ] }""";
    }

    private static GeminiResaleEstimator Build(HttpMessageHandler handler, IMemoryCache cache)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" })
            .Build();
        return new GeminiResaleEstimator(new HttpClient(handler), config, cache, TimeProvider.System, NullLogger<GeminiResaleEstimator>.Instance);
    }

    [Fact]
    public async Task Parses_estimate_from_response()
    {
        CountingHandler handler = new(Envelope("""{"low":150,"high":220,"rationale":"eBay sold avg"}"""));
        GeminiResaleEstimator sut = Build(handler, new MemoryCache(new MemoryCacheOptions()));

        ResaleEstimate? result = await sut.EstimateAsync("Sony WH-1000XM4", null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(150m, result.Low);
        Assert.Equal(220m, result.High);
        Assert.Equal("eBay sold avg", result.Rationale);
    }

    [Fact]
    public async Task Parses_sold_and_active_comps()
    {
        string inner = """
        {
          "low": 120, "high": 180, "priceRangeType": "moderate", "valuationConfidence": "medium",
          "comps": [
            { "price": 130, "status": "sold", "condition": "Good", "note": "recent eBay sold" },
            { "price": 175, "status": "active", "condition": "Like new", "note": "current asking" },
            { "price": 0, "status": "sold" }
          ]
        }
        """;
        GeminiResaleEstimator sut = Build(new CountingHandler(Envelope(inner)), new MemoryCache(new MemoryCacheOptions()));

        ResaleEstimate? result = await sut.EstimateAsync("Sony WH-1000XM4", null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Comps);
        Assert.Equal(2, result.Comps!.Count); // zero-price comp dropped
        Assert.Contains(result.Comps, c => c.Status == "sold" && c.Price == 130m);
        Assert.Contains(result.Comps, c => c.Status == "active" && c.Price == 175m);
    }

    [Fact]
    public async Task Asks_the_grounded_search_for_ebay_sold_listings_of_the_normalized_query()
    {
        CountingHandler handler = new(Envelope("""{"low":55,"high":80}"""));
        GeminiResaleEstimator sut = Build(handler, new MemoryCache(new MemoryCacheOptions()));
        ResaleSearchHints hints = ResaleSearchHintBuilder.FromQuery("Pokemon Gardevoir Sylveon GX 130/214 Unbroken Bonds");

        ResaleEstimate? result = await sut.EstimateAsync(
            "Pokemon Gardevoir Sylveon GX 130/214 Unbroken Bonds",
            hints,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("Search eBay sold listings for: Pokemon Gardevoir Sylveon GX 130/214 Unbroken Bonds", handler.LastRequestBody);
    }

    [Fact]
    public async Task Caches_by_product_so_repeat_calls_hit_no_api()
    {
        CountingHandler handler = new(Envelope("""{"low":10,"high":20}"""));
        MemoryCache cache = new(new MemoryCacheOptions());
        GeminiResaleEstimator sut = Build(handler, cache);

        ResaleEstimate? a = await sut.EstimateAsync("Pyrex 4-Cup Measuring Cup", null, CancellationToken.None);
        ResaleEstimate? b = await sut.EstimateAsync("  pyrex   4-cup   measuring cup ", null, CancellationToken.None); // same, normalized

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a.Low, b.Low);
        Assert.Equal(1, handler.CallCount); // second call served from cache
    }

    [Fact]
    public async Task Different_products_call_api_separately()
    {
        CountingHandler handler = new(Envelope("""{"low":10,"high":20}"""));
        MemoryCache cache = new(new MemoryCacheOptions());
        GeminiResaleEstimator sut = Build(handler, cache);

        await sut.EstimateAsync("Product A", null, CancellationToken.None);
        await sut.EstimateAsync("Product B", null, CancellationToken.None);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Same_model_different_variant_does_not_share_cache()
    {
        // The exact risk: same product line, different colorway/value. Must NOT collide.
        CountingHandler handler = new(Envelope("""{"low":100,"high":400}"""));
        MemoryCache cache = new(new MemoryCacheOptions());
        GeminiResaleEstimator sut = Build(handler, cache);

        await sut.EstimateAsync("Nike Air Jordan 1 Chicago 2015", null, CancellationToken.None);
        await sut.EstimateAsync("Nike Air Jordan 1 Bred 2023", null, CancellationToken.None);
        await sut.EstimateAsync("Nike Air Jordan 1 Royal", null, CancellationToken.None);

        Assert.Equal(3, handler.CallCount); // each variant estimated separately
    }

    [Fact]
    public async Task Same_name_only_punctuation_or_case_differs_shares_cache()
    {
        // Truly identical product (same question) → one estimate, no wasted call.
        CountingHandler handler = new(Envelope("""{"low":150,"high":220}"""));
        MemoryCache cache = new(new MemoryCacheOptions());
        GeminiResaleEstimator sut = Build(handler, cache);

        await sut.EstimateAsync("Sony WH-1000XM4!", null, CancellationToken.None);
        await sut.EstimateAsync("  sony   wh 1000xm4 ", null, CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Not_configured_without_key()
    {
        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        GeminiResaleEstimator sut = new(new HttpClient(new CountingHandler("{}")), config,
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<GeminiResaleEstimator>.Instance);

        Assert.False(sut.IsConfigured);
        Assert.Null(await sut.EstimateAsync("anything", null, CancellationToken.None));
    }
}
