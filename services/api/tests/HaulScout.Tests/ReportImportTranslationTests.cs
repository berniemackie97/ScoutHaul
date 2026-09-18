using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Sync;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Organizations;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Sync;

namespace HaulScout.Tests;

/// <summary>
/// Imports against the database that serves them.
///
/// The in-memory provider hides exactly the differences change detection has to survive: Postgres
/// returns money at its column's scale and times in UTC, so an identical re-import can look changed
/// if the comparison is naive. It also enforces the foreign keys the in-memory provider ignores —
/// which is how an unsaved artifact went unnoticed until these ran.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportImportTranslationTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class AllowAll : IOrganizationAuthorizer
    {
        public Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task RequireOwner(Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static ReportImportService Imports(HaulScoutDbContext db, Guid org, Guid user)
    {
        StubCurrentUser currentUser = new(org, user);
        OrderAssembler assembler = new(db, currentUser);
        EfPlatformOrderStore orderStore = new(db, assembler);
        PlatformOrderSuggestionService suggestions = new(new EfPlatformOrderSuggestionStore(db, currentUser));
        PlatformOrderWorkflowService workflow = new(orderStore, currentUser, suggestions);
        PlatformOrderAutoMatchService autoMatch = new(suggestions, workflow, orderStore, currentUser);
        return new ReportImportService(
            new EfSyncRunStore(db), currentUser, new AllowAll(), new EfOrganizationStore(db),
            new PlatformOrderIngestionService(orderStore, currentUser, suggestions, workflow, autoMatch),
            new PlatformOrderFeeEnrichmentService(orderStore, currentUser),
            new MarketplaceRefundImportService(orderStore, currentUser, TimeProvider.System),
            new PlatformListingImportService(db, currentUser),
            TimeProvider.System,
            NullLogger<ReportImportService>.Instance);
    }

    private const string DepopHeader =
        "\"Date of sale\",\"Time of sale\",\"Date of listing\",\"Bundle\",\"Bundle - amount of items\",\"Buyer\",\"Brand\",\"SKU\",\"Description\",\"Size\",\"Item price\",\"Buyer shipping cost\",\"Total\",\"USPS Cost\",\"Depop fee\",\"Depop Payments fee\",\"Buyer Marketplace Fee\",\"Boosting fee\",\"Payment type\",\"Estimated payout date\",\"Payout arrival date\",\"Category\",\"Name\",\"Address Line 1\",\"Address Line 2\",\"City\",\"State\",\"Post Code\",\"Country\",\"US Sales tax\",\"Refunded to buyer amount\",\"Fees refunded to seller\"";

    // "$40" with no cents on purpose: parsed as 40, read back from numeric as 40.00.
    private const string DepopRow =
        "\"07/03/2026\",\"1:46 PM\",\"07/01/2026\",\"No\",\"N/A\",\"buyer_one\",\"Levi's\",\"N/A\",\"Vintage jeans\",\"34\",\"$40\",\"$6\",\"$49.20\",\"$5.10\",\"$0.00\",\"$1.60\",\"$1.75\",\"=\"\"-\"\"\",\"STRIPE\",\"07/07/2026\",\"N/A\",\"Bottoms\",\"Test Buyer\",\"1 Test Street\",\"\",\"Springfield\",\"OR\",\"97477\",\"US\",\"$3.20\",\"$0.00\",\"$0.00\"";

    private static ReportFile Depop() => new("Depop Sales.csv", Encoding.UTF8.GetBytes(DepopHeader + "\n" + DepopRow));

    // eBay's own date format, zone and all, exactly as a real Active Listings export prints it.
    private static ReportFile EbayListings() => new("eBay-all-active-listings.csv", Encoding.UTF8.GetBytes(
        "Item number,Title,Current price,Available quantity,Sold quantity,Start date,End date\n" +
        "111,\"Brass lamp\",$40,1,0,Jun-10-26 13:11:11 PDT,Jul-10-26 13:11:11 PDT\n"));

    [Fact]
    public async Task An_identical_order_re_import_is_unchanged_against_Postgres()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        SyncRunDto first = Assert.Single((await Imports(db, org, user).ImportAsync([Depop()], CancellationToken.None)).Files).Run!;
        db.ChangeTracker.Clear();
        SyncRunDto second = Assert.Single((await Imports(db, org, user).ImportAsync([Depop()], CancellationToken.None)).Files).Run!;

        Assert.Equal(1, first.Created);
        Assert.Equal((0, 0, 1), (second.Created, second.Updated, second.Unchanged));
    }

    [Fact]
    public async Task An_identical_listing_re_import_is_unchanged_against_Postgres()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        await Imports(db, org, user).ImportAsync([EbayListings()], CancellationToken.None);
        db.ChangeTracker.Clear();
        SyncRunDto second = Assert.Single((await Imports(db, org, user).ImportAsync([EbayListings()], CancellationToken.None)).Files).Run!;

        Assert.True(second.Status != "Failed", second.Summary);
        Assert.Equal((0, 0, 1), (second.Created, second.Updated, second.Unchanged));
    }

    [Fact]
    public async Task eBay_listing_dates_are_kept_and_stored()
    {
        // They used to be dropped, and an offset date would have failed the save outright.
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        SyncRunDto run = Assert.Single((await Imports(db, org, user).ImportAsync([EbayListings()], CancellationToken.None)).Files).Run!;
        db.ChangeTracker.Clear();

        Assert.Equal("Succeeded", run.Status);
        Domain.Entities.PlatformListing listing = await db.PlatformListings.AsNoTracking().SingleAsync();
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 20, 11, 11, TimeSpan.Zero), listing.ListedAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 20, 11, 11, TimeSpan.Zero), listing.EndsAt);
    }

    [Fact]
    public async Task History_and_the_kept_file_translate_to_sql()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        ReportImportService imports = Imports(db, org, user);
        ReportFile file = Depop();

        SyncRunDto run = Assert.Single((await imports.ImportAsync([file], CancellationToken.None)).Files).Run!;
        db.ChangeTracker.Clear();

        SyncRunListDto history = await imports.ListAsync(1, 25, CancellationToken.None);
        Assert.Equal("Depop Sales.csv", Assert.Single(history.Items).FileName);

        ReportDetectionDto detected = Assert.Single(await imports.DetectAsync([file], CancellationToken.None));
        Assert.NotNull(detected.PreviouslyImportedAt);

        Assert.Equal(file.Content, (await imports.DownloadAsync(run.Id, CancellationToken.None)).Value!.Content);
    }
}
