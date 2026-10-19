using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Appraisal;
using HaulScout.Infrastructure.Vision;

namespace HaulScout.Tests.Vision;

public sealed class GeminiListingCopyWriterTests
{
    private sealed class CaptureHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static string Envelope(string innerJson)
    {
        string escaped = System.Text.Json.JsonSerializer.Serialize(innerJson);
        return $$"""{ "candidates": [ { "content": { "parts": [ { "text": {{escaped}} } ] } } ] }""";
    }

    private static GeminiListingCopyWriter Writer(CaptureHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "test-key" }).Build(),
        NullLogger<GeminiListingCopyWriter>.Instance);

    [Fact]
    public async Task Writes_the_title_and_description_and_shows_the_model_only_the_comps_used()
    {
        CaptureHandler handler = new(Envelope("""{"title":"Sony WH-1000XM4 Wireless Headphones Black","description":"Noise-cancelling, works perfectly."}"""));
        AppraisalDto appraisal = new(
            "Sony WH-1000XM4",
            new PriceDto(PriceBasis.Sales, 50m, 59.99m, 65m, 70, ConfidenceLevel.Fair, []),
            [
                new CompDto(CompKind.Sold, "eBay", "Sony WH-1000XM4 Black", 60m, null, "Used", null, null, true, true, null),
                new CompDto(CompKind.Sold, "eBay", "Sony WH-1000XM3", 35m, null, "Used", null, null, true, false, "different model"),
            ],
            null, [], [], [], DateTimeOffset.UtcNow);

        ListingCopy? copy = await Writer(handler).WriteAsync("Sony WH-1000XM4 headphones", appraisal, CancellationToken.None);

        Assert.Equal("Sony WH-1000XM4 Wireless Headphones Black", copy?.Title);
        Assert.Contains("Sony WH-1000XM4 Black", handler.RequestBody!);
        Assert.DoesNotContain("WH-1000XM3", handler.RequestBody!);
    }

    [Fact]
    public async Task A_failed_answer_is_no_copy_rather_than_an_empty_one()
    {
        CaptureHandler handler = new("""{"error":"bad"}""", HttpStatusCode.BadRequest);

        Assert.Null(await Writer(handler).WriteAsync("Anything", null, CancellationToken.None));
    }
}
