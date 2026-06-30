using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Infrastructure.Vision;

namespace HaulScout.Tests.Vision;

/// <summary>
/// Tests the Gemini multimodal product identifier — the primary identification path.
/// Each fixture mimics a real Gemini generateContent response (structured JSON inside
/// candidates[0].content.parts[0].text) for a given product photo scenario.
/// </summary>
public sealed class GeminiProductVisionTests
{
    private static async Task<ProductIdentification?> Identify(string geminiJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" })
            .Build();

        var service = new GeminiProductVisionService(
            new HttpClient(new FakeHandler(geminiJson, status)),
            config,
            NullLogger<GeminiProductVisionService>.Instance);

        // IdentifyAsync now returns a VisionIdentifyResult; tests assert on the Product it wraps.
        return (await service.IdentifyAsync(new string('A', 200), null, CancellationToken.None)).Product;
    }

    private static string GeminiEnvelope(string innerJson)
    {
        // Gemini returns the model's JSON as a string inside parts[0].text
        string escaped = System.Text.Json.JsonSerializer.Serialize(innerJson);
        return $$"""
        { "candidates": [ { "content": { "parts": [ { "text": {{escaped}} } ] } } ] }
        """;
    }

    private sealed class FakeHandler(string responseJson, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class CaptureHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    // Returns the queued responses in order; repeats the last one once exhausted.
    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var r = responses[Math.Min(CallCount, responses.Length - 1)];
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(r.Status)
            {
                Content = new StringContent(r.Body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static GeminiProductVisionService ServiceWith(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" })
            .Build();
        return new GeminiProductVisionService(new HttpClient(handler), config, NullLogger<GeminiProductVisionService>.Instance);
    }

    [Fact]
    public async Task Text_only_identification_sends_adaptive_prompt_without_image_part()
    {
        string ok = GeminiEnvelope("""{"name":"Sony WH-1000XM4 Headphones","brand":"Sony","model":"WH-1000XM4","category":"Electronics","confidence":"high","searchKeywords":["Sony WH-1000XM4"]}""");
        var handler = new CaptureHandler(ok);

        ProductIdentification? result = (await ServiceWith(handler).IdentifyAsync(
            null,
            "Sony WH-1000XM4 headphones, black, used good condition",
            CancellationToken.None)).Product;

        Assert.NotNull(result);
        Assert.Equal("Sony", result.Brand);
        Assert.Contains("there is NO photo", handler.RequestBody!);
        Assert.Contains("Known facts provided by the user", handler.RequestBody!);
        Assert.DoesNotContain("inline_data", handler.RequestBody!);
    }

    [Fact]
    public async Task Photo_with_context_sends_ground_truth_facts_and_image_part()
    {
        string ok = GeminiEnvelope("""{"name":"Vintage Pyrex Bowl","category":"Kitchen","confidence":"medium","searchKeywords":["vintage pyrex bowl"]}""");
        var handler = new CaptureHandler(ok);

        ProductIdentification? result = (await ServiceWith(handler).IdentifyAsync(
            new string('A', 200),
            "User says the bottom stamp reads Pyrex 403, yellow, mixing bowl",
            CancellationToken.None)).Product;

        Assert.NotNull(result);
        Assert.Contains("PHOTO of the item AND known facts", handler.RequestBody!);
        Assert.Contains("Known facts provided by the user", handler.RequestBody!);
        Assert.Contains("inline_data", handler.RequestBody!);
    }

    [Fact]
    public async Task Retries_once_on_429_then_succeeds()
    {
        string ok = GeminiEnvelope("""{"name":"Lego Set","category":"Toys","confidence":"high","searchKeywords":["lego"]}""");
        var handler = new SequenceHandler(
            (HttpStatusCode.TooManyRequests, """{"error":"rate limited"}"""),
            (HttpStatusCode.OK, ok));

        ProductIdentification? result = (await ServiceWith(handler).IdentifyAsync(new string('A', 200), null, CancellationToken.None)).Product;

        Assert.NotNull(result);
        Assert.Equal("Lego Set", result.Name);
        Assert.Equal(2, handler.CallCount); // retried exactly once
    }

    [Fact]
    public async Task Throws_rate_limited_after_four_429s()
    {
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, "{}"));
        var ex = await Assert.ThrowsAsync<VisionUnavailableException>(() =>
            ServiceWith(handler).IdentifyAsync(new string('A', 200), null, CancellationToken.None));

        Assert.Equal("rate_limited", ex.Code);
        Assert.Equal(4, handler.CallCount); // initial + three retries (exponential backoff), then throw
    }

    [Fact]
    public async Task Throws_quota_exhausted_immediately_on_daily_cap()
    {
        // PerDay quota won't clear in seconds → fail fast, no retries wasted.
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests,
            """{"error":{"status":"RESOURCE_EXHAUSTED","details":[{"violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]}]}}"""));
        var ex = await Assert.ThrowsAsync<VisionUnavailableException>(() =>
            ServiceWith(handler).IdentifyAsync(new string('A', 200), null, CancellationToken.None));

        Assert.Equal("quota_exhausted", ex.Code);
        Assert.Equal(1, handler.CallCount); // no retries on a daily-quota wall
    }

    [Fact]
    public async Task Identifies_specific_branded_product()
    {
        string inner = """
        {
          "name": "Sony WH-1000XM4 Wireless Noise Cancelling Headphones",
          "brand": "Sony",
          "model": "WH-1000XM4",
          "category": "Electronics",
          "color": "Black",
          "condition": "Used - Good",
          "estimatedResaleLow": 120,
          "estimatedResaleHigh": 180,
          "identifyingText": "SONY WH-1000XM4",
          "searchKeywords": ["Sony WH-1000XM4", "Sony XM4 headphones", "Sony noise cancelling headphones"],
          "confidence": "high"
        }
        """;

        ProductIdentification? result = await Identify(GeminiEnvelope(inner));

        Assert.NotNull(result);
        Assert.Equal("Sony WH-1000XM4 Wireless Noise Cancelling Headphones", result.Name);
        Assert.Equal("Sony", result.Brand);
        Assert.Equal("WH-1000XM4", result.Model);
        Assert.Equal("Electronics", result.Category);
        Assert.Equal(120, result.EstimatedResaleLow);
        Assert.Equal(180, result.EstimatedResaleHigh);
        Assert.Equal("high", result.Confidence);
        Assert.Equal(3, result.SearchKeywords.Count);
    }

    [Fact]
    public async Task Identifies_generic_item_with_low_confidence()
    {
        // A plain grey rocking chair with no branding
        string inner = """
        {
          "name": "Wooden Rocking Chair",
          "category": "Furniture",
          "color": "Brown",
          "condition": "Used",
          "estimatedResaleLow": 40,
          "estimatedResaleHigh": 90,
          "searchKeywords": ["wooden rocking chair", "vintage rocking chair"],
          "confidence": "low"
        }
        """;

        ProductIdentification? result = await Identify(GeminiEnvelope(inner));

        Assert.NotNull(result);
        Assert.Equal("Wooden Rocking Chair", result.Name);
        Assert.Equal("Furniture", result.Category);
        Assert.Null(result.Brand);
        Assert.Equal("low", result.Confidence);
        Assert.True(result.EstimatedResaleLow > 0);
    }

    [Fact]
    public async Task Normalizes_unknown_field_values_to_null()
    {
        string inner = """
        {
          "name": "Coffee Mug",
          "brand": "unknown",
          "model": "N/A",
          "category": "Home",
          "color": "none",
          "searchKeywords": ["coffee mug"],
          "confidence": "medium"
        }
        """;

        ProductIdentification? result = await Identify(GeminiEnvelope(inner));

        Assert.NotNull(result);
        Assert.Null(result.Brand);   // "unknown" → null
        Assert.Null(result.Model);   // "N/A" → null
        Assert.Null(result.Color);   // "none" → null
        Assert.Equal("Home", result.Category);
    }

    [Fact]
    public async Task Clamps_invalid_confidence_to_low()
    {
        string inner = """
        { "name": "Thing", "category": "Misc", "searchKeywords": ["thing"], "confidence": "extremely-sure" }
        """;

        ProductIdentification? result = await Identify(GeminiEnvelope(inner));

        Assert.NotNull(result);
        Assert.Equal("low", result.Confidence);
    }

    [Fact]
    public async Task Caps_search_keywords_at_six()
    {
        string inner = """
        {
          "name": "Item",
          "category": "Misc",
          "confidence": "medium",
          "searchKeywords": ["a","b","c","d","e","f","g","h"]
        }
        """;

        ProductIdentification? result = await Identify(GeminiEnvelope(inner));

        Assert.NotNull(result);
        Assert.Equal(6, result.SearchKeywords.Count);
    }

    [Fact]
    public async Task Throws_on_persistent_api_error()
    {
        // A real provider error (not a 200-with-no-match) must surface as an exception so the client
        // can show the cause, never a misleading "not identified".
        var handler = new FakeHandler("""{"error":{"code":429}}""", HttpStatusCode.TooManyRequests);
        await Assert.ThrowsAsync<VisionUnavailableException>(() =>
            ServiceWith(handler).IdentifyAsync(new string('A', 200), null, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_null_on_malformed_response()
    {
        ProductIdentification? result = await Identify("not json at all");
        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_when_no_candidates()
    {
        ProductIdentification? result = await Identify("""{"candidates":[]}""");
        Assert.Null(result);
    }

    [Fact]
    public async Task Not_configured_without_api_key()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var service = new GeminiProductVisionService(new HttpClient(new FakeHandler("{}", HttpStatusCode.OK)), config, NullLogger<GeminiProductVisionService>.Instance);

        Assert.False(service.IsConfigured);
        Assert.Null((await service.IdentifyAsync("AAAA", null, CancellationToken.None)).Product);
    }
}
