using Microsoft.Extensions.Logging.Abstractions;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Infrastructure.Vision;

namespace ScoutHaul.Tests.Vision;

/// <summary>
/// Tests the composite that merges Gemini product identification with Cloud Vision web matches,
/// including the search-query priority logic used for marketplace comp lookups.
/// </summary>
public sealed class CompositeVisionTests
{
    private sealed class StubIdentifier(ProductIdentification? result, bool configured = true) : IProductIdentifier
    {
        public bool IsConfigured => configured;
        public int CallCount { get; private set; }
        public Task<ProductIdentification?> IdentifyAsync(string imageBase64, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubMatcher(WebMatchResult result, bool configured = true) : IWebImageMatcher
    {
        public bool IsConfigured => configured;
        public int CallCount { get; private set; }
        public Task<WebMatchResult> MatchAsync(string imageBase64, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private static CompositeVisionIdentificationService Build(IProductIdentifier id, IWebImageMatcher matcher) =>
        new(id, matcher, NullLogger<CompositeVisionIdentificationService>.Instance);

    private static ProductIdentification Product(
        string? name = "Sony WH-1000XM4", string? brand = "Sony", string? model = "WH-1000XM4",
        string[]? keywords = null, string confidence = "high") =>
        new(name, brand, model, "Electronics", "Black", "Used", 120, 180, "SONY",
            keywords ?? ["Sony XM4"], confidence);

    [Fact]
    public async Task Runs_both_providers_and_merges()
    {
        var id = new StubIdentifier(Product());
        var matcher = new StubMatcher(new WebMatchResult(
            "sony headphones", [], [new VisionMatchingPage("https://ebay.com/itm/1", "Sony XM4 - eBay")], [], ["Headphones"], ["Sony"], null));

        VisionIdentifyResult result = await Build(id, matcher).IdentifyAsync("img", CancellationToken.None);

        Assert.Equal(1, id.CallCount);
        Assert.Equal(1, matcher.CallCount);
        Assert.NotNull(result.Product);
        Assert.Single(result.MatchingPages);
        Assert.Equal("Sony WH-1000XM4", result.BestGuessLabel); // product name wins over web bestGuess
    }

    [Fact]
    public async Task Search_query_prefers_brand_and_model()
    {
        var id = new StubIdentifier(Product(brand: "Sony", model: "WH-1000XM4"));
        var matcher = new StubMatcher(WebMatchResult.Empty);

        VisionIdentifyResult result = await Build(id, matcher).IdentifyAsync("img", CancellationToken.None);

        Assert.Equal("Sony WH-1000XM4", result.SearchQuery);
    }

    [Fact]
    public async Task Search_query_falls_back_to_name_when_no_model()
    {
        var id = new StubIdentifier(Product(name: "Vintage Pyrex Bowl", brand: null, model: null));
        var matcher = new StubMatcher(WebMatchResult.Empty);

        VisionIdentifyResult result = await Build(id, matcher).IdentifyAsync("img", CancellationToken.None);

        Assert.Equal("Vintage Pyrex Bowl", result.SearchQuery);
    }

    [Fact]
    public async Task Search_query_falls_back_to_web_when_no_product()
    {
        var id = new StubIdentifier(null);
        var matcher = new StubMatcher(new WebMatchResult(
            "antique brass lamp", [], [], [], [], [], null));

        VisionIdentifyResult result = await Build(id, matcher).IdentifyAsync("img", CancellationToken.None);

        Assert.Equal("antique brass lamp", result.SearchQuery);
        Assert.Null(result.Product);
    }

    [Fact]
    public async Task Skips_unconfigured_providers()
    {
        var id = new StubIdentifier(Product(), configured: false);
        var matcher = new StubMatcher(WebMatchResult.Empty, configured: false);

        var sut = Build(id, matcher);
        VisionIdentifyResult result = await sut.IdentifyAsync("img", CancellationToken.None);

        Assert.False(sut.IsConfigured);
        Assert.Equal(0, id.CallCount);
        Assert.Equal(0, matcher.CallCount);
        Assert.Null(result.Product);
        Assert.Null(result.SearchQuery);
    }

    [Fact]
    public async Task Configured_when_only_gemini_available()
    {
        var sut = Build(new StubIdentifier(Product()), new StubMatcher(WebMatchResult.Empty, configured: false));
        Assert.True(sut.IsConfigured);
    }

    [Fact]
    public async Task Works_with_only_gemini_no_web_matcher()
    {
        var id = new StubIdentifier(Product());
        var matcher = new StubMatcher(WebMatchResult.Empty, configured: false);

        VisionIdentifyResult result = await Build(id, matcher).IdentifyAsync("img", CancellationToken.None);

        Assert.Equal(1, id.CallCount);
        Assert.Equal(0, matcher.CallCount);
        Assert.NotNull(result.Product);
        Assert.Empty(result.MatchingPages);
        Assert.Equal("Sony WH-1000XM4", result.SearchQuery);
    }
}
