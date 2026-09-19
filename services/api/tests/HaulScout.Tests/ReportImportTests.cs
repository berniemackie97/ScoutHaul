using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Sync;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Organizations;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Sync;

namespace HaulScout.Tests;

/// <summary>
/// One drop zone for every export, and a record of every import.
///
/// Imports used to be four upload cards the operator had to choose between, with nothing left
/// behind but their effects — "did I already import July?" had no answer. These pin the replacement:
/// files recognised by their contents, previewed before anything changes, imported independently,
/// and every attempt recorded with its counts, its problem rows and the exact file.
/// </summary>
public sealed class ReportImportTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class StubAuthorizer(params string[] granted) : IOrganizationAuthorizer
    {
        public Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            granted.Contains(permission) ? Task.CompletedTask : throw new UnauthorizedAccessException(permission);

        public Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(granted.Contains(permission));

        public Task RequireOwner(Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static readonly string[] Everything = [HaulScoutPermissions.ManageFinance, HaulScoutPermissions.ManageListings];

    private sealed record Harness(ReportImportService Imports, HaulScoutDbContext Db, Guid Org);

    private static Harness NewHarness(params string[] granted)
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();

        StubCurrentUser currentUser = new(org, user);
        OrderAssembler assembler = new(db, currentUser);
        EfPlatformOrderStore orderStore = new(db, assembler);
        PlatformOrderSuggestionService suggestions = new(new EfPlatformOrderSuggestionStore(db, currentUser));
        PlatformOrderWorkflowService workflow = new(orderStore, currentUser, suggestions);
        PlatformOrderAutoMatchService autoMatch = new(suggestions, workflow, orderStore, currentUser);

        ReportImportService imports = new(
            new EfSyncRunStore(db),
            currentUser,
            new StubAuthorizer(granted),
            new EfOrganizationStore(db),
            new PlatformOrderIngestionService(orderStore, currentUser, suggestions, workflow, autoMatch),
            new PlatformOrderFeeEnrichmentService(orderStore, currentUser),
            new MarketplaceRefundImportService(orderStore, currentUser, TimeProvider.System),
            new PlatformListingImportService(db, currentUser),
            orderStore,
            TimeProvider.System,
            NullLogger<ReportImportService>.Instance);

        return new Harness(imports, db, org);
    }

    // ── Fixtures: real headers, synthetic rows ─────────────────────────────────

    private const string DepopHeader =
        "\"Date of sale\",\"Time of sale\",\"Date of listing\",\"Bundle\",\"Bundle - amount of items\",\"Buyer\",\"Brand\",\"SKU\",\"Description\",\"Size\",\"Item price\",\"Buyer shipping cost\",\"Total\",\"USPS Cost\",\"Depop fee\",\"Depop Payments fee\",\"Buyer Marketplace Fee\",\"Boosting fee\",\"Payment type\",\"Estimated payout date\",\"Payout arrival date\",\"Category\",\"Name\",\"Address Line 1\",\"Address Line 2\",\"City\",\"State\",\"Post Code\",\"Country\",\"US Sales tax\",\"Refunded to buyer amount\",\"Fees refunded to seller\"";

    private static string DepopRow(string description, string price = "$40.00", string bundle = "No") =>
        string.Join(",", new[]
        {
            "07/03/2026", "1:46 PM", "07/01/2026", bundle, bundle == "Yes" ? "2" : "N/A", "buyer_one", "Levi's", "N/A", description, "34",
            price, "$6.00", "$49.20", "$5.10", "$0.00", "$1.60", "$1.75", "=\"-\"", "STRIPE", "07/07/2026", "N/A", "Bottoms",
            "Test Buyer", "1 Test Street", "", "Springfield", "OR", "97477", "US", "$3.20", "$0.00", "$0.00",
        }.Select(cell => $"\"{cell.Replace("\"", "\"\"")}\""));

    private static ReportFile DepopFile(params string[] rows) =>
        new("Depop Sales.csv", Encoding.UTF8.GetBytes(string.Join("\n", [DepopHeader, .. rows])));

    private static ReportFile PoshmarkFile(params string[] titles) => new("poshmark.csv", Encoding.UTF8.GetBytes(string.Join("\n",
        [
            "Poshmark Inventory Report for @testcloset",
            "Inventory as of 07/09/2026",
            "\"\"",
            "Listing Date,Number of Days Listed,Listing SKU,Item SKU,Listing Title,Department,Category,Subcategory,Brand,Color,Size,NWT,Quantity Available,Likes,Cost Price,Lowest Listed Price,Current Listing Price,Other Info,Seller Shipping Discount",
            .. titles.Select(title => $"06/23/2026,16,,,{title},Women,Tops,Blouses,Chico's,Yellow,2,N,1,3,\"\",$25.00,$25.00,,\"\""),
        ])));

    private static ReportFile EbayActiveListingsFile(params (string Id, string Title, string Price)[] rows) =>
        new("eBay-all-active-listings.csv", Encoding.UTF8.GetBytes(string.Join("\n",
        [
            "Item number,Title,Current price,Available quantity,Sold quantity",
            .. rows.Select(row => $"{row.Id},\"{row.Title}\",{row.Price},1,0"),
        ])));

    private static readonly ReportFile NotAnExport = new("notes.csv", Encoding.UTF8.GetBytes("name,age\nann,3"));

    // ── Detection ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Detection_recognises_each_file_by_its_contents_and_writes_nothing()
    {
        Harness harness = NewHarness(Everything);

        IReadOnlyList<ReportDetectionDto> detected = await harness.Imports.DetectAsync(
            [DepopFile(DepopRow("Jeans")), PoshmarkFile("Blouse", "Dress"), NotAnExport], CancellationToken.None);

        Assert.Equal("depop.sales", detected[0].FormatKey);
        Assert.Equal(1, detected[0].Records);
        Assert.True(detected[0].CanImport);
        Assert.Equal("poshmark.inventory", detected[1].FormatKey);
        Assert.Equal(2, detected[1].Records);
        Assert.True(detected[1].CompleteSnapshot);
        Assert.Null(detected[2].FormatKey);
        Assert.False(detected[2].CanImport);
        Assert.NotNull(detected[2].Problem);

        Assert.Equal(0, await harness.Db.SyncRuns.CountAsync());
        Assert.Equal(0, await harness.Db.PlatformOrders.CountAsync());
    }

    [Fact]
    public async Task Detection_says_which_files_the_caller_s_role_cannot_import()
    {
        // A listings-only role can drop a mixed batch and see exactly which files are theirs to import.
        Harness harness = NewHarness(HaulScoutPermissions.ManageListings);

        IReadOnlyList<ReportDetectionDto> detected = await harness.Imports.DetectAsync(
            [DepopFile(DepopRow("Jeans")), PoshmarkFile("Blouse")], CancellationToken.None);

        Assert.False(detected[0].CanImport);
        Assert.Contains("role", detected[0].Problem);
        Assert.True(detected[1].CanImport);
    }

    // ── Importing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Imports_a_Depop_export_and_records_the_run()
    {
        Harness harness = NewHarness(Everything);

        ImportResultDto result = await harness.Imports.ImportAsync(
            [DepopFile(DepopRow("Jeans"), DepopRow("Wool coat", bundle: "Yes"))], CancellationToken.None);

        SyncRunDto run = Assert.Single(result.Files).Run!;
        Assert.Equal("depop.sales", run.FormatKey);
        Assert.Equal(2, run.Created);
        Assert.Equal("CompletedWithIssues", run.Status);
        Assert.Equal(1, run.IssueCount);
        Assert.Equal(2, await harness.Db.PlatformOrders.CountAsync(order => order.Platform == "Depop"));

        SyncRunIssue issue = Assert.Single(await harness.Db.SyncRunIssues.ToListAsync());
        Assert.Equal(SyncIssueSeverity.Warning, issue.Severity);
        Assert.Contains("Bundle", issue.Message);
    }

    [Fact]
    public async Task Importing_the_same_file_again_changes_nothing_and_says_so()
    {
        Harness harness = NewHarness(Everything);
        ReportFile file = DepopFile(DepopRow("Jeans"));
        await harness.Imports.ImportAsync([file], CancellationToken.None);

        ReportDetectionDto detected = Assert.Single(await harness.Imports.DetectAsync([file], CancellationToken.None));
        SyncRunDto second = Assert.Single((await harness.Imports.ImportAsync([file], CancellationToken.None)).Files).Run!;

        Assert.NotNull(detected.PreviouslyImportedAt);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(1, await harness.Db.PlatformOrders.CountAsync());
        // The file is kept once, however many times it is imported.
        Assert.Equal(1, await harness.Db.SyncArtifacts.CountAsync());
    }

    [Fact]
    public async Task Keeps_the_exact_file_it_read()
    {
        Harness harness = NewHarness(Everything);
        ReportFile file = DepopFile(DepopRow("Jeans"));

        SyncRunDto run = Assert.Single((await harness.Imports.ImportAsync([file], CancellationToken.None)).Files).Run!;
        AppResult<ReportFile> download = await harness.Imports.DownloadAsync(run.Id, CancellationToken.None);

        Assert.Equal(file.Content, download.Value!.Content);
        Assert.Equal("Depop Sales.csv", download.Value.FileName);
    }

    [Fact]
    public async Task Imports_Poshmark_as_a_complete_snapshot_of_its_date()
    {
        Harness harness = NewHarness(Everything);

        SyncRunDto run = Assert.Single((await harness.Imports.ImportAsync([PoshmarkFile("Blouse", "Dress")], CancellationToken.None)).Files).Run!;

        Assert.Equal(2, run.Created);
        Assert.True(run.IsCompleteSnapshot);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 7, 0, 0, TimeSpan.Zero), run.CoversTo);
        Assert.Equal(2, await harness.Db.PlatformListings.CountAsync(listing => listing.Platform == "Poshmark"));
    }

    [Fact]
    public async Task One_bad_file_does_not_stop_the_others()
    {
        Harness harness = NewHarness(Everything);

        ImportResultDto result = await harness.Imports.ImportAsync([NotAnExport, DepopFile(DepopRow("Jeans"))], CancellationToken.None);

        Assert.Equal("Failed", result.Files[0].Run!.Status);
        Assert.Equal("unrecognised", result.Files[0].Run!.FormatKey);
        Assert.Equal(1, result.Files[1].Run!.Created);
        // The unreadable file is in the history, with the file, rather than vanishing.
        Assert.Equal(2, await harness.Db.SyncRuns.CountAsync());
        Assert.Equal(2, await harness.Db.SyncArtifacts.CountAsync());
    }

    [Fact]
    public async Task Refuses_an_oversized_file_by_name_without_recording_a_run()
    {
        Harness harness = NewHarness(Everything);
        ReportFile huge = new("huge.csv", new byte[ReportImportService.MaxFileBytes + 1]);

        ImportFileOutcomeDto outcome = Assert.Single((await harness.Imports.ImportAsync([huge], CancellationToken.None)).Files);

        Assert.Null(outcome.Run);
        Assert.Contains("MB", outcome.Problem);
        Assert.Equal(0, await harness.Db.SyncRuns.CountAsync());
    }

    [Fact]
    public async Task Refuses_a_file_the_caller_s_role_cannot_import()
    {
        Harness harness = NewHarness(HaulScoutPermissions.ManageListings);

        ImportFileOutcomeDto outcome = Assert.Single((await harness.Imports.ImportAsync([DepopFile(DepopRow("Jeans"))], CancellationToken.None)).Files);

        Assert.Null(outcome.Run);
        Assert.Equal(0, await harness.Db.PlatformOrders.CountAsync());
    }

    [Fact]
    public async Task Rerunning_reads_the_kept_file_again()
    {
        Harness harness = NewHarness(Everything);
        SyncRunDto first = Assert.Single((await harness.Imports.ImportAsync([DepopFile(DepopRow("Jeans"))], CancellationToken.None)).Files).Run!;

        AppResult<SyncRunDto> rerun = await harness.Imports.RerunAsync(first.Id, CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, rerun.Status);
        Assert.Equal(first.Id, rerun.Value!.RerunOfId);
        Assert.Equal(1, rerun.Value.Unchanged);
    }

    [Fact]
    public async Task History_lists_newest_first_with_issue_counts()
    {
        Harness harness = NewHarness(Everything);
        await harness.Imports.ImportAsync([PoshmarkFile("Blouse")], CancellationToken.None);
        await harness.Imports.ImportAsync([DepopFile(DepopRow("Coat", bundle: "Yes"))], CancellationToken.None);

        SyncRunListDto history = await harness.Imports.ListAsync(1, 25, includeQuiet: false, CancellationToken.None);

        Assert.Equal(2, history.Total);
        Assert.Equal("depop.sales", history.Items[0].FormatKey);
        Assert.Equal(1, history.Items[0].IssueCount);
        Assert.Equal("Depop Sales.csv", history.Items[0].FileName);

        SyncRunDetailDto detail = (await harness.Imports.GetAsync(history.Items[0].Id, CancellationToken.None)).Value!;
        Assert.Contains("Bundle", Assert.Single(detail.Issues).Message);
    }

    // ── Listing refresh counts ─────────────────────────────────────────────────

    [Fact]
    public async Task An_identical_listings_export_counts_as_unchanged_and_a_new_price_as_updated()
    {
        // Every re-imported listing used to count as updated, so the history could not say what changed.
        Harness harness = NewHarness(Everything);
        await harness.Imports.ImportAsync([EbayActiveListingsFile(("111", "Lamp", "$40.00"), ("222", "Chair", "$90.00"))], CancellationToken.None);

        SyncRunDto same = Assert.Single((await harness.Imports.ImportAsync(
            [EbayActiveListingsFile(("111", "Lamp", "$40.00"), ("222", "Chair", "$90.00"))], CancellationToken.None)).Files).Run!;
        SyncRunDto repriced = Assert.Single((await harness.Imports.ImportAsync(
            [EbayActiveListingsFile(("111", "Lamp", "$35.00"), ("222", "Chair", "$90.00"))], CancellationToken.None)).Files).Run!;

        Assert.Equal((0, 0, 2), (same.Created, same.Updated, same.Unchanged));
        Assert.Equal((0, 1, 1), (repriced.Created, repriced.Updated, repriced.Unchanged));
    }

    // ── Queues ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Counts_what_imports_left_for_a_person_to_match()
    {
        Harness harness = NewHarness(Everything);
        await harness.Imports.ImportAsync([DepopFile(DepopRow("Jeans"), DepopRow("Coat")), PoshmarkFile("Blouse")], CancellationToken.None);

        ImportQueuesDto queues = await harness.Imports.QueuesAsync(CancellationToken.None);

        Assert.Equal(2, queues.OrdersToMatch);
        Assert.Equal(1, queues.ListingsToMatch);
    }

    [Fact]
    public async Task Hides_a_queue_the_caller_s_role_cannot_work()
    {
        Harness harness = NewHarness(HaulScoutPermissions.ManageListings);

        ImportQueuesDto queues = await harness.Imports.QueuesAsync(CancellationToken.None);

        Assert.Null(queues.OrdersToMatch);
        Assert.Equal(0, queues.ListingsToMatch);
    }

    // ── Failure ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_failed_import_is_recorded_without_its_half_written_data()
    {
        Harness harness = NewHarness(Everything);
        EfSyncRunStore store = new(harness.Db);
        SyncRun run = new()
        {
            OrganizationId = harness.Org,
            FormatKey = "depop.sales",
            Platform = "Depop",
            StartedAt = DateTimeOffset.UtcNow,
        };
        store.Add(run);
        await store.SaveChangesAsync(CancellationToken.None);

        // Something an import left pending when it threw.
        harness.Db.PlatformOrders.Add(new PlatformOrder { OrganizationId = harness.Org, Platform = "Depop", ExternalOrderId = "half-written" });

        await store.FailAsync(harness.Org, run.Id, "The import stopped part-way.", DateTimeOffset.UtcNow, CancellationToken.None);

        SyncRun saved = await harness.Db.SyncRuns.AsNoTracking().SingleAsync();
        Assert.Equal(SyncRunStatus.Failed, saved.Status);
        Assert.NotNull(saved.FinishedAt);
        Assert.Equal(0, await harness.Db.PlatformOrders.CountAsync());
    }
}
