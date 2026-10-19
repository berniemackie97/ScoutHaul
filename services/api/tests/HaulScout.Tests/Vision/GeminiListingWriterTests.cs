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
        public Task<ResaleEstimate?> EstimateAsync(string productDescription, ResaleSearchHints? searchHints, CancellationToken cancellationToken) =>
            Task.FromResult(estimate);
        public Task<ResaleSearchOutcome> SearchAsync(string productDescription, ResaleSearchHints? searchHints, CancellationToken cancellationToken) =>
            Task.FromResult(new ResaleSearchOutcome(estimate, null));
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
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" })
            .Build();
        return new GeminiListingWriter(
            new HttpClient(new FakeHandler(Envelope(copyJson))),
            config,
            new StubEstimator(estimate),
            NullLogger<GeminiListingWriter>.Instance);
    }

    [Fact]
    public async Task Generates_title_description_and_deterministic_comp_backed_price()
    {
        ResaleEstimate estimate = new(
            Low: 40m,
            High: 65m,
            Rationale: "based on recent sold comps",
            ValuationConfidence: "medium",
            PriceRangeType: "moderate",
            Comps: [new ResaleComp(60m, "sold", "good", "eBay sold", null)]);

        GeminiListingWriter writer = NewWriter(
            """{"title":"Sony WH-1000XM4 Wireless Headphones Black Good Used","description":"Great noise-cancelling headphones, works perfectly.","listPrice":62,"priceRationale":"just under recent sold comps"}""",
            estimate);

        GeneratedListingDto? result = await writer.GenerateAsync(
            new GenerateListingRequest("Sony WH-1000XM4", Brand: "Sony", Condition: "Good"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sony WH-1000XM4 Wireless Headphones Black Good Used", result!.ListingTitle);
        Assert.Contains("noise-cancelling", result.ListingDescription);
        Assert.Equal(59.99m, result.SuggestedListPrice);
        Assert.Equal(65m, result.EstimatedResaleHigh);
        Assert.Single(result.Comps);
        Assert.Contains("sold comp", result.PriceRationale);
    }

    [Fact]
    public async Task Ignores_model_lowball_and_filters_bad_collectible_comps()
    {
        ResaleEstimate estimate = new(
            Low: 50m,
            High: 80m,
            Rationale: "recent eBay sold comps",
            ValuationConfidence: "high",
            PriceRangeType: "moderate",
            Comps:
            [
                new ResaleComp(59.99m, "sold", "Near Mint", "eBay sold Jul 1 2026 — Pokemon Gardevoir & Sylveon GX TAG TEAM 130/214 SM Unbroken Bonds Holo NEAR MINT", "https://www.ebay.com/itm/1"),
                new ResaleComp(56m, "sold", "NM", "eBay sold Jun 30 2026 — Pokemon TCG Gardevoir & Sylveon GX Holo Card 130/214 Unbroken Bonds NM", "https://www.ebay.com/itm/2"),
                new ResaleComp(65m, "sold", null, "eBay sold Jun 30 2026 — Pokemon Gardevoir & Sylveon GX Unbroken Bonds Holo Card 130/214", "https://www.ebay.com/itm/3"),
                new ResaleComp(0.93m, "sold", null, "eBay sold Jun 30 2026 — Basic Gardevoir & Sylveon GX Tag Team HP 260 Gold Foil Fan Art Card", "https://www.ebay.com/itm/4"),
                new ResaleComp(179m, "sold", "PSA 9", "eBay sold — 2019 Pokemon Sun Moon Unbroken Bonds #130 Gardevoir & Sylveon GX PSA 9", "https://www.ebay.com/itm/5"),
                new ResaleComp(79.99m, "sold", "NM", "eBay sold Jun 30 2026 — Gardevoir & Sylveon GX Tag Team 130/214 Sm-Unbroken Bonds Rare Holo - NM", "https://www.ebay.com/itm/6"),
                new ResaleComp(60m, "sold", null, "eBay sold Jun 30 2026 — Gardevoir & Sylveon GX 130/214 Holo Unbroken Bonds", "https://www.ebay.com/itm/7"),
            ]);

        GeminiListingWriter writer = NewWriter(
            """{"title":"Pokemon TCG Gardevoir & Sylveon GX 130/214 Unbroken Bonds NM","description":"Gardevoir & Sylveon GX TAG TEAM from SM Unbroken Bonds. Near mint raw card; see photos for exact condition.","listPrice":22,"priceRationale":"priced conservatively"}""",
            estimate);

        GeneratedListingDto? result = await writer.GenerateAsync(
            new GenerateListingRequest(
                "Gardevoir & Sylveon GX 130/214",
                Brand: "Pokemon",
                Category: "Collectibles",
                Condition: "Near Mint",
                AttributesJson: """{"set":"SM Unbroken Bonds","cardNumber":"130/214","variant":"TAG TEAM Holo","grading":"Raw"}"""),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(64.99m, result!.SuggestedListPrice);
        Assert.Equal(5, result.Comps.Count);
        Assert.DoesNotContain(result.Comps, comp => comp.Price == 0.93m);
        Assert.DoesNotContain(result.Comps, comp => comp.Condition?.Contains("PSA", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains("$56", result.PriceRationale);
        Assert.Contains("$79.99", result.PriceRationale);
    }

    [Fact]
    public async Task Falls_back_to_estimate_range_when_model_omits_price_and_no_sold_comps_exist()
    {
        ResaleEstimate estimate = new(Low: 30m, High: 50m, Rationale: null, PriceRangeType: "wide");

        GeminiListingWriter writer = NewWriter("""{"title":"Vintage Brass Lamp","description":"A brass table lamp."}""", estimate);

        GeneratedListingDto? result = await writer.GenerateAsync(
            new GenerateListingRequest("Vintage Brass Lamp"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(41.99m, result!.SuggestedListPrice);
    }
}
