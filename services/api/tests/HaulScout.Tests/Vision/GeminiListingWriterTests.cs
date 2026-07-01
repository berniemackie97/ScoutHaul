using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Infrastructure.Vision;

namespace HaulScout.Tests.Vision;

public sealed class GeminiListingWriterTests
{
    private sealed class StubEstimator(ResaleEstimate? estimate) : IResaleEstimator
    {
        public bool IsConfigured => true;
        public Task<ResaleEstimate?> EstimateAsync(string productDescription, CancellationToken cancellationToken) =>
            Task.FromResult(estimate);
    }

    private sealed class FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static string Envelope(string innerJson)
    {
        string escaped = System.Text.Json.JsonSerializer.Serialize(innerJson);
        return $$"""{ "candidates": [ { "content": { "parts": [ { "text": {{escaped}} } ] } } ] }""";
    }

    private static GeminiListingWriter NewWriter(string copyJson, ResaleEstimate? estimate)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" })
            .Build();
        return new GeminiListingWriter(
            new HttpClient(new FakeHandler(Envelope(copyJson))),
            config,
            new StubEstimator(estimate),
            NullLogger<GeminiListingWriter>.Instance);
    }

    [Fact]
    public async Task Generates_title_description_and_comp_backed_price()
    {
        var estimate = new ResaleEstimate(
            Low: 40m,
            High: 65m,
            Rationale: "based on recent sold comps",
            ValuationConfidence: "medium",
            PriceRangeType: "moderate",
            Comps: [new ResaleComp(60m, "sold", "good", "eBay sold", null)]);

        var writer = NewWriter(
            """{"title":"Sony WH-1000XM4 Wireless Headphones Black Good Used","description":"Great noise-cancelling headphones, works perfectly.","listPrice":62,"priceRationale":"just under recent sold comps"}""",
            estimate);

        GeneratedListingDto? result = await writer.GenerateAsync(
            new GenerateListingRequest("Sony WH-1000XM4", Brand: "Sony", Condition: "Good"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sony WH-1000XM4 Wireless Headphones Black Good Used", result!.ListingTitle);
        Assert.Contains("noise-cancelling", result.ListingDescription);
        Assert.Equal(62m, result.SuggestedListPrice);
        Assert.Equal(65m, result.EstimatedResaleHigh);
        Assert.Single(result.Comps);
    }

    [Fact]
    public async Task Falls_back_to_estimate_high_when_model_omits_price()
    {
        var estimate = new ResaleEstimate(Low: 30m, High: 50m, Rationale: null, PriceRangeType: "wide");

        var writer = NewWriter("""{"title":"Vintage Brass Lamp","description":"A brass table lamp."}""", estimate);

        GeneratedListingDto? result = await writer.GenerateAsync(
            new GenerateListingRequest("Vintage Brass Lamp"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(50m, result!.SuggestedListPrice); // no listPrice in copy → estimate.High
    }
}
