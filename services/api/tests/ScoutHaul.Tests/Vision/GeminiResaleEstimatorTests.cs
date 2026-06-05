using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Infrastructure.Vision;

namespace ScoutHaul.Tests.Vision;

public sealed class GeminiResaleEstimatorTests
{
    private sealed class CountingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string Envelope(string innerJson)
    {
        string escaped = System.Text.Json.JsonSerializer.Serialize(innerJson);
        return $$"""{ "candidates": [ { "content": { "parts": [ { "text": {{escaped}} } ] } } ] }""";
    }

    private static GeminiResaleEstimator Build(HttpMessageHandler handler, IMemoryCache cache)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" })
            .Build();
        return new GeminiResaleEstimator(new HttpClient(handler), config, cache, NullLogger<GeminiResaleEstimator>.Instance);
    }

    [Fact]
    public async Task Parses_estimate_from_response()
    {
        var handler = new CountingHandler(Envelope("""{"low":150,"high":220,"rationale":"eBay sold avg"}"""));
        var sut = Build(handler, new MemoryCache(new MemoryCacheOptions()));

        ResaleEstimate? result = await sut.EstimateAsync("Sony WH-1000XM4", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(150m, result.Low);
        Assert.Equal(220m, result.High);
        Assert.Equal("eBay sold avg", result.Rationale);
    }

    [Fact]
    public async Task Caches_by_product_so_repeat_calls_hit_no_api()
    {
        var handler = new CountingHandler(Envelope("""{"low":10,"high":20}"""));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = Build(handler, cache);

        var a = await sut.EstimateAsync("Pyrex 4-Cup Measuring Cup", CancellationToken.None);
        var b = await sut.EstimateAsync("  pyrex   4-cup   measuring cup ", CancellationToken.None); // same, normalized

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a.Low, b.Low);
        Assert.Equal(1, handler.CallCount); // second call served from cache
    }

    [Fact]
    public async Task Different_products_call_api_separately()
    {
        var handler = new CountingHandler(Envelope("""{"low":10,"high":20}"""));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = Build(handler, cache);

        await sut.EstimateAsync("Product A", CancellationToken.None);
        await sut.EstimateAsync("Product B", CancellationToken.None);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Same_model_different_variant_does_not_share_cache()
    {
        // The exact risk: same product line, different colorway/value. Must NOT collide.
        var handler = new CountingHandler(Envelope("""{"low":100,"high":400}"""));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = Build(handler, cache);

        await sut.EstimateAsync("Nike Air Jordan 1 Chicago 2015", CancellationToken.None);
        await sut.EstimateAsync("Nike Air Jordan 1 Bred 2023", CancellationToken.None);
        await sut.EstimateAsync("Nike Air Jordan 1 Royal", CancellationToken.None);

        Assert.Equal(3, handler.CallCount); // each variant estimated separately
    }

    [Fact]
    public async Task Same_name_only_punctuation_or_case_differs_shares_cache()
    {
        // Truly identical product (same question) → one estimate, no wasted call.
        var handler = new CountingHandler(Envelope("""{"low":150,"high":220}"""));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = Build(handler, cache);

        await sut.EstimateAsync("Sony WH-1000XM4!", CancellationToken.None);
        await sut.EstimateAsync("  sony   wh 1000xm4 ", CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Not_configured_without_key()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var sut = new GeminiResaleEstimator(new HttpClient(new CountingHandler("{}")), config,
            new MemoryCache(new MemoryCacheOptions()), NullLogger<GeminiResaleEstimator>.Instance);

        Assert.False(sut.IsConfigured);
        Assert.Null(await sut.EstimateAsync("anything", CancellationToken.None));
    }
}
