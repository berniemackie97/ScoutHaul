using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Tests;

public sealed class AppraisalServiceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_barcode_the_business_has_on_file_is_identified_from_its_own_catalog()
    {
        Guid productId = Guid.NewGuid();
        FakeCatalog catalog = new(new CatalogProduct(productId, "Sony WH-1000XM5", "Sony", "WH-1000XM5", "Electronics", true));
        FakeVision vision = new();

        AppResult<IdentificationDto> result = await Service(catalog: catalog, vision: vision)
            .IdentifyAsync(new IdentifyInput(null, "0 27242-92353 4", null), CancellationToken.None);

        IdentificationDto identified = Assert.IsType<IdentificationDto>(result.Value);
        Assert.Equal(IdentificationSource.Catalog, identified.Source);
        Assert.Equal(productId, identified.ProductIdentityId);
        Assert.Equal("027242923534", identified.Barcode);
        Assert.Equal(IdentificationCertainty.High, identified.Certainty);
        Assert.Equal(0, vision.Calls);
    }

    [Fact]
    public async Task An_unknown_barcode_says_so_and_points_at_a_photo()
    {
        AppResult<IdentificationDto> result = await Service()
            .IdentifyAsync(new IdentifyInput(null, "012345678905", null), CancellationToken.None);

        Assert.Equal(AppResultStatus.NotFound, result.Status);
        Assert.Equal("barcode_unknown", result.Code);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abc")]
    public async Task A_barcode_that_cannot_be_one_is_refused(string barcode)
    {
        AppResult<IdentificationDto> result = await Service()
            .IdentifyAsync(new IdentifyInput(null, barcode, null), CancellationToken.None);

        Assert.Equal("barcode_invalid", result.Code);
    }

    [Fact]
    public async Task An_empty_barcode_field_beside_details_is_ignored()
    {
        FakeVision vision = new(Product("Pyrex 403 mixing bowl", confidence: "medium"));

        AppResult<IdentificationDto> result = await Service(vision: vision)
            .IdentifyAsync(new IdentifyInput(null, " ", "Yellow Pyrex bowl stamped 403"), CancellationToken.None);

        Assert.Equal(IdentificationSource.Details, Assert.IsType<IdentificationDto>(result.Value).Source);
    }

    [Fact]
    public async Task A_photo_goes_to_vision_with_its_real_type_and_a_printed_barcode_is_kept()
    {
        FakeVision vision = new(Product("Nintendo 64 Controller", confidence: "high", identifyingText: "NUS-005 045496870058"));

        AppResult<IdentificationDto> result = await Service(vision: vision).IdentifyAsync(
            new IdentifyInput(new AppraisalPhoto([0x89, 0x50], "image/png"), null, null), CancellationToken.None);

        IdentificationDto identified = Assert.IsType<IdentificationDto>(result.Value);
        Assert.Equal("image/png", vision.LastMediaType);
        Assert.Equal("045496870058", identified.Barcode);
        Assert.Equal(IdentificationSource.Photo, identified.Source);
    }

    [Fact]
    public async Task Details_the_model_cannot_place_are_not_dressed_up_as_an_identification()
    {
        AppResult<IdentificationDto> result = await Service(vision: new FakeVision(null))
            .IdentifyAsync(new IdentifyInput(null, null, "blue thing"), CancellationToken.None);

        Assert.Equal(AppResultStatus.NotFound, result.Status);
        Assert.Equal("not_identified", result.Code);
    }

    [Fact]
    public async Task One_failing_source_is_reported_and_the_others_still_price_the_item()
    {
        AppraisalService service = Service(sources:
        [
            new FakeSource("Your sales", comps: [Sale(190m), Sale(200m), Sale(210m)]),
            new FakeSource("Web search", failure: new CompSourceUnavailableException("Web search has used today's quota.")),
            new FakeSource("eBay listings", configured: false),
        ]);

        AppraisalDto appraisal = (await service.AppraiseAsync(Request(), CancellationToken.None)).Value!;

        Assert.Equal(PriceBasis.Sales, appraisal.Price.Basis);
        Assert.Contains(appraisal.Sources, report => report is { Name: "Your sales", Status: CompSourceStatus.Found, Count: 3 });
        Assert.Contains(appraisal.Sources, report => report is { Name: "Web search", Status: CompSourceStatus.Failed, Message: "Web search has used today's quota." });
        Assert.Contains(appraisal.Sources, report => report is { Name: "eBay listings", Status: CompSourceStatus.Off });
    }

    [Fact]
    public async Task A_source_past_its_deadline_is_cut_off_rather_than_holding_the_answer()
    {
        AppraisalService service = Service(sources:
        [
            new FakeSource("Slow", hangs: true, deadline: TimeSpan.FromMilliseconds(50)),
            new FakeSource("Your sales", comps: [Sale(200m)]),
        ]);

        AppraisalDto appraisal = (await service.AppraiseAsync(Request(), CancellationToken.None)).Value!;

        Assert.Contains(appraisal.Sources, report => report is { Name: "Slow", Status: CompSourceStatus.Failed, Message: "Took too long to answer." });
        Assert.Equal(PriceBasis.Sales, appraisal.Price.Basis);
    }

    [Fact]
    public async Task The_identification_estimate_is_used_only_when_no_source_has_one()
    {
        EstimateDto fromPhoto = new(80m, 120m, "medium", "From the photo.", false);

        AppraisalDto withoutWeb = (await Service().AppraiseAsync(Request() with { Estimate = fromPhoto }, CancellationToken.None)).Value!;
        AppraisalDto withWeb = (await Service(sources:
            [new FakeSource("Web search", estimate: new ModelEstimate(150m, 170m, "From listings.", "high"))])
            .AppraiseAsync(Request() with { Estimate = fromPhoto }, CancellationToken.None)).Value!;

        Assert.Equal(PriceBasis.Estimate, withoutWeb.Price.Basis);
        Assert.Equal(99.99m, withoutWeb.Price.List);
        Assert.Equal(159.99m, withWeb.Price.List);
    }

    [Fact]
    public async Task The_same_listing_reported_by_two_sources_counts_once()
    {
        Comp listing = Sale(200m) with { Url = "https://www.ebay.com/itm/1" };
        AppraisalService service = Service(sources:
            [new FakeSource("A", comps: [listing]), new FakeSource("B", comps: [listing with { Url = "HTTPS://WWW.EBAY.COM/itm/1" }])]);

        AppraisalDto appraisal = (await service.AppraiseAsync(Request(), CancellationToken.None)).Value!;

        Assert.Single(appraisal.Comps);
    }

    [Fact]
    public async Task An_item_with_no_name_is_not_priced()
    {
        AppResult<AppraisalDto> result = await Service().AppraiseAsync(Request() with { Title = " " }, CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, result.Status);
    }

    [Fact]
    public async Task Search_links_and_text_name_the_brand_and_model()
    {
        AppraisalDto appraisal = (await Service().AppraiseAsync(
            new AppraiseRequest("WH-1000XM5 headphones", Brand: "Sony", Model: "WH-1000XM5"), CancellationToken.None)).Value!;

        Assert.Equal("Sony WH-1000XM5 headphones", appraisal.SearchText);
        Assert.StartsWith("https://www.ebay.com/sch/i.html?_nkw=Sony%20WH-1000XM5%20headphones&LH_Sold=1", appraisal.SearchLinks[0].Url);
    }

    [Theory]
    [InlineData("AI Comp", true)]
    [InlineData("ai comp (web)", true)]
    [InlineData("AI", true)]
    [InlineData("eBay", false)]
    [InlineData("Aidan's shop", false)]
    public void Only_a_model_labelled_source_is_treated_as_one(string source, bool isModel)
    {
        Assert.Equal(isModel, CompHonesty.IsModelSource(source));
    }

    private static AppraiseRequest Request() =>
        new("Sony WH-1000XM5 Wireless Headphones", Brand: "Sony", Model: "WH-1000XM5", Certainty: IdentificationCertainty.High);

    private static Comp Sale(decimal price) =>
        new(CompKind.YourSale, "Your sale", "Sony WH-1000XM5 Headphones", price, null, "Good", null, Now.AddDays(-20), true);

    private static ProductIdentification Product(string name, string confidence, string? identifyingText = null) =>
        new(name, Brand: null, Model: null, Category: null, Color: null, Condition: null,
            EstimatedResaleLow: null, EstimatedResaleHigh: null, identifyingText, SearchKeywords: [], confidence);

    private static AppraisalService Service(
        FakeCatalog? catalog = null,
        FakeVision? vision = null,
        IEnumerable<ICompSource>? sources = null) =>
        new(
            catalog ?? new FakeCatalog(null),
            new FakeBarcodes(),
            vision ?? new FakeVision(),
            sources ?? [],
            new StubUser(),
            new FakeTimeProvider(Now),
            NullLogger<AppraisalService>.Instance);

    private sealed class FakeCatalog(CatalogProduct? product) : IProductCatalogStore
    {
        public Task<CatalogProduct?> FindByBarcodeAsync(Guid organizationId, string barcode, CancellationToken cancellationToken) =>
            Task.FromResult(product);
    }

    private sealed class FakeBarcodes : IBarcodeLookupService
    {
        public Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<BarcodeLookupResult?>(null);
    }

    private sealed class FakeVision(ProductIdentification? product = null) : IVisionIdentificationService
    {
        public int Calls { get; private set; }
        public string? LastMediaType { get; private set; }
        public bool IsConfigured => true;

        public Task<VisionIdentifyResult> IdentifyAsync(string? imageBase64, string? imageMediaType, string? context, CancellationToken cancellationToken)
        {
            Calls++;
            LastMediaType = imageMediaType;
            return Task.FromResult(new VisionIdentifyResult(product, product?.Name, product?.IdentifyingText));
        }
    }

    private sealed class FakeSource(
        string name,
        IReadOnlyList<Comp>? comps = null,
        ModelEstimate? estimate = null,
        Exception? failure = null,
        bool configured = true,
        bool hangs = false,
        TimeSpan? deadline = null) : ICompSource
    {
        public string Name => name;
        public bool IsConfigured => configured;
        public TimeSpan Deadline => deadline ?? TimeSpan.FromSeconds(5);

        public async Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken)
        {
            if (hangs)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (failure is not null)
            {
                throw failure;
            }

            return new CompSourceResult(comps ?? [], estimate);
        }
    }

    private sealed class StubUser : ICurrentUserContext
    {
        public Guid OrganizationId => Org;
        public Guid? ActiveOrganizationId => Org;
        public Guid UserId => Guid.Empty;
        public Task ResolveAsync() => Task.CompletedTask;
    }
}
