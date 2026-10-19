using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Tests;

public sealed class ListingGenerationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_listing_price_is_the_appraisal_price_and_attributes_pin_the_variant()
    {
        CapturingSource source = new(
        [
            new Comp(CompKind.Sold, "eBay", "Japanese Gardevoir & Sylveon GX 130/214", 34m, null, null, null, Now.AddDays(-5), true),
            new Comp(CompKind.Sold, "eBay", "JPN Gardevoir & Sylveon GX 130/214", 36m, null, null, null, Now.AddDays(-9), true),
            new Comp(CompKind.Sold, "eBay", "English Gardevoir & Sylveon GX 130/214 NM", 65m, null, null, null, Now.AddDays(-3), true),
        ]);
        ListingGenerationService service = Service(source, new StubWriter(new ListingCopy("Gardevoir & Sylveon GX 130/214 Japanese", "Clean card.")));

        GeneratedListingDto listing = (await service.GenerateAsync(
            new GenerateListingRequest(
                "Japanese Gardevoir & Sylveon GX 130/214",
                Brand: "Pokemon",
                AttributesJson: """{"language":"Japanese","cardNumber":"130/214"}"""),
            CancellationToken.None)).Value!;

        Assert.Equal(35.99m, listing.SuggestedListPrice);
        Assert.Equal(PriceBasis.Sales, listing.PriceBasis);
        Assert.Equal(2, listing.Comps.Count);
        Assert.Contains("Japanese", source.LastQuery!.Item.Identifiers);
    }

    [Fact]
    public async Task No_copy_from_the_writer_is_a_failure_the_caller_can_name()
    {
        ListingGenerationService service = Service(new CapturingSource([]), new StubWriter(null));

        AppResult<GeneratedListingDto> result = await service.GenerateAsync(new GenerateListingRequest("Brass lamp"), CancellationToken.None);

        Assert.Equal("generation_failed", result.Code);
    }

    private static ListingGenerationService Service(ICompSource source, IListingCopyWriter writer) => new(
        new AppraisalService(
            new NoCatalog(), new NoBarcodes(), new NoVision(), [source], new NoHistory(), new StubUser(),
            new FakeTimeProvider(Now), NullLogger<AppraisalService>.Instance),
        writer);

    private sealed class CapturingSource(IReadOnlyList<Comp> comps) : ICompSource
    {
        public CompQuery? LastQuery { get; private set; }
        public string Name => "Test";
        public bool IsConfigured => true;
        public TimeSpan Deadline => TimeSpan.FromSeconds(5);

        public Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new CompSourceResult(comps));
        }
    }

    private sealed class StubWriter(ListingCopy? copy) : IListingCopyWriter
    {
        public bool IsConfigured => true;
        public Task<ListingCopy?> WriteAsync(string details, AppraisalDto? appraisal, CancellationToken cancellationToken) => Task.FromResult(copy);
    }

    internal sealed class NoHistory : IAppraisalHistoryStore
    {
        public Task<bool> ItemExistsAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task RecordAsync(Guid organizationId, Guid inventoryItemId, Guid? productIdentityId, PriceResult price, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AppraisalSnapshotDto>> ListForItemAsync(Guid organizationId, Guid inventoryItemId, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AppraisalSnapshotDto>>([]);
    }

    private sealed class NoCatalog : IProductCatalogStore
    {
        public Task<CatalogProduct?> FindByBarcodeAsync(Guid organizationId, string barcode, CancellationToken cancellationToken) => Task.FromResult<CatalogProduct?>(null);
    }

    private sealed class NoBarcodes : IBarcodeLookupService
    {
        public Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default) => Task.FromResult<BarcodeLookupResult?>(null);
    }

    private sealed class NoVision : IVisionIdentificationService
    {
        public bool IsConfigured => false;
        public Task<VisionIdentifyResult> IdentifyAsync(string? imageBase64, string? imageMediaType, string? context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    private sealed class StubUser : ICurrentUserContext
    {
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public Guid? ActiveOrganizationId => OrganizationId;
        public Guid UserId => Guid.Empty;
        public Task ResolveAsync() => Task.CompletedTask;
    }
}
