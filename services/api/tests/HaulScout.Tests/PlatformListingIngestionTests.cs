using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

using HaulScout.Application.Channels;

using HaulScout.Infrastructure.Channels;

namespace HaulScout.Tests;

public class PlatformListingIngestionTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org, Guid User) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static IngestPlatformListingInput Listing(string id, string title, decimal price, PlatformListingStatus status = PlatformListingStatus.Active) =>
        new(id, title, price, status);

    [Fact]
    public async Task A_listings_photos_are_kept_with_a_key_and_a_source_that_does_not_read_them_erases_nothing()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_listings_photos_are_kept_with_a_key_and_a_source_that_does_not_read_them_erases_nothing));
        await using HaulScoutDbContext _ = db;
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        string[] photos = ["https://i.ebayimg.com/images/g/a/s-l1600.jpg", "https://i.ebayimg.com/images/g/b/s-l1600.jpg"];

        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 40m) with { PhotoUrls = photos }]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();
        string? key = listing.PhotoUrlsKey;
        DateTimeOffset? changed = listing.PhotoUrlsChangedAt;
        Assert.NotNull(key);
        Assert.Contains("s-l1600", listing.PhotoUrlsJson);

        // The same photos again: nothing to re-read, so the key and the moment it changed stay as they were.
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 40m) with { PhotoUrls = photos }]), CancellationToken.None);
        listing = await db.PlatformListings.SingleAsync();
        Assert.Equal((key, changed), (listing.PhotoUrlsKey, listing.PhotoUrlsChangedAt));

        // A source that does not read photos at all leaves the ones already known alone.
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 41m)]), CancellationToken.None);
        listing = await db.PlatformListings.SingleAsync();
        Assert.Equal(key, listing.PhotoUrlsKey);

        // A different set is a change, and is noticed.
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 41m) with { PhotoUrls = [photos[0]] }]), CancellationToken.None);
        listing = await db.PlatformListings.SingleAsync();
        Assert.NotEqual(key, listing.PhotoUrlsKey);
        Assert.True(listing.PhotoUrlsChangedAt >= changed);
    }


    [Fact]
    public async Task A_listing_that_sells_again_after_leaving_sold_is_asked_about_again()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_listing_that_sells_again_after_leaving_sold_is_asked_about_again));
        await using HaulScoutDbContext _ = db;
        StubCurrentUser currentUser = new(org, user);
        PlatformListingsController controller = new(db, currentUser, new PlatformListingImportService(db, currentUser), new SoldListingService(new EfSoldListingStore(db), currentUser, TimeProvider.System));

        await controller.Ingest(new IngestPlatformListingsRequest("Poshmark", [Listing("PM-1", "Denim Jacket", 40m, PlatformListingStatus.Sold)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();
        DateTimeOffset? firstSold = listing.MarkedSoldAt;
        Assert.NotNull(firstSold);

        // Answered, then relisted through a status that is neither Sold nor Active: both signals go with the sale.
        listing.SoldAcknowledgedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await controller.Ingest(new IngestPlatformListingsRequest("Poshmark", [Listing("PM-1", "Denim Jacket", 40m, PlatformListingStatus.Ended)]), CancellationToken.None);
        listing = await db.PlatformListings.SingleAsync();
        Assert.Null(listing.MarkedSoldAt);
        Assert.Null(listing.SoldAcknowledgedAt);

        // Selling again is news of its own, not something the old answer covers.
        await controller.Ingest(new IngestPlatformListingsRequest("Poshmark", [Listing("PM-1", "Denim Jacket", 40m, PlatformListingStatus.Sold)]), CancellationToken.None);
        listing = await db.PlatformListings.SingleAsync();
        Assert.NotNull(listing.MarkedSoldAt);
        Assert.Null(listing.SoldAcknowledgedAt);
    }

    [Fact]
    public async Task A_linked_item_learns_what_its_listing_says_about_it()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_linked_item_learns_what_its_listing_says_about_it));
        await using HaulScoutDbContext _ = db;
        StubCurrentUser currentUser = new(org, user);
        PlatformListingsController controller = new(db, currentUser, new PlatformListingImportService(db, currentUser), new SoldListingService(new EfSoldListingStore(db), currentUser, TimeProvider.System));

        InventoryItem item = new() { OrganizationId = org, Title = "Denim Jacket", Brand = "Levi's" };
        db.InventoryItems.Add(item);
        db.PlatformListings.Add(new PlatformListing
        {
            OrganizationId = org,
            Platform = "eBay",
            ExternalListingId = "EB-1",
            Title = "Denim Jacket",
            ReconcileStatus = PlatformListingReconcileStatus.Linked,
            ReconciledInventoryItemId = item.Id,
        });
        await db.SaveChangesAsync();

        await controller.Ingest(
            new IngestPlatformListingsRequest("eBay", [
                Listing("EB-1", "Denim Jacket", 40m) with
                {
                    Brand = "Wrangler",
                    Color = "Indigo",
                    Size = "L",
                    CategoryName = "Coats & Jackets",
                    Description = "A jacket.",
                },
            ]),
            CancellationToken.None);

        InventoryItem filled = await db.InventoryItems.SingleAsync();
        Assert.Equal("Clothing", filled.Category);
        Assert.Equal(["Blue"], filled.Colors);
        Assert.Equal(("US letter", "L"), (filled.SizeSystem, filled.SizeValue));
        Assert.Equal("A jacket.", filled.Description);
        // The brand someone typed stands; the listing does not get to correct them.
        Assert.Equal("Levi's", filled.Brand);
    }

    [Fact]
    public async Task Ingest_creates_then_reingest_updates_in_place()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Ingest_creates_then_reingest_updates_in_place));
        await using HaulScoutDbContext _ = db;
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));

        ActionResult<IngestPlatformListingsResultDto> first = await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 40m)]), CancellationToken.None);
        Assert.Equal(1, first.Value!.Created);

        ActionResult<IngestPlatformListingsResultDto> second = await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 45m)]), CancellationToken.None);
        Assert.Equal(0, second.Value!.Created);
        Assert.Equal(1, second.Value.Updated);

        PlatformListing listing = Assert.Single(await db.PlatformListings.ToListAsync());
        Assert.Equal(45m, listing.Price);
    }

    [Fact]
    public async Task Reconcile_create_new_seeds_a_draft_item_and_a_channel_listing()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_create_new_seeds_a_draft_item_and_a_channel_listing));
        await using HaulScoutDbContext _ = db;
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-2", "Vintage Camera", 120m)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();

        ActionResult<PlatformListingReconcileResultDto> result = await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(CreateNewItem: true), CancellationToken.None);

        PlatformListingReconcileResultDto reconcile = result.Value!;
        Assert.True(reconcile.CreatedInventoryItem);
        InventoryItem item = await db.InventoryItems.SingleAsync(i => i.Id == reconcile.InventoryItemId);
        Assert.Equal("Vintage Camera", item.Title);
        Assert.Equal(InventoryStatus.Listed, item.Status);      // active listing → listed
        Assert.Equal(120m, item.EstimatedSalePrice);

        ChannelListing channel = await db.ChannelListings.SingleAsync(c => c.Id == reconcile.ChannelListingId);
        Assert.Equal("EB-2", channel.ExternalListingId);
        Assert.Equal(ChannelListingStatus.Active, channel.Status);

        PlatformListing linked = await db.PlatformListings.SingleAsync();
        Assert.Equal(PlatformListingReconcileStatus.Linked, linked.ReconcileStatus);
        Assert.Equal(item.Id, linked.ReconciledInventoryItemId);
        Assert.True(linked.CreatedChannelListingOnReconcile);
    }

    [Fact]
    public async Task Relisting_an_item_reuses_its_channel_listing_instead_of_violating_the_unique_key()
    {
        // The real-world case: an item is listed, ends, and is relisted, so eBay issues a NEW item
        // number for the same item. Looking the channel listing up by external id found nothing and
        // inserted a second row for the same (item, account) — which the unique index rejects, and
        // the operator saw "An error occurred while processing your request".
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(
            nameof(Relisting_an_item_reuses_its_channel_listing_instead_of_violating_the_unique_key));
        await using HaulScoutDbContext _ = db;

        InventoryItem item = new() { OrganizationId = org, Title = "Mesh Maxi Dress", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));

        // First listing, linked.
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-OLD", "Mesh Maxi Dress", 75m)]), CancellationToken.None);
        PlatformListing first = await db.PlatformListings.SingleAsync(candidate => candidate.ExternalListingId == "EB-OLD");
        await controller.Reconcile(first.Id, new ReconcilePlatformListingRequest(InventoryItemId: item.Id), CancellationToken.None);

        // Relisted under a new platform item number, linked to the same item.
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-NEW", "Mesh Maxi Dress", 75m)]), CancellationToken.None);
        PlatformListing relisted = await db.PlatformListings.SingleAsync(candidate => candidate.ExternalListingId == "EB-NEW");

        ActionResult<PlatformListingReconcileResultDto> result = await controller.Reconcile(
            relisted.Id, new ReconcilePlatformListingRequest(InventoryItemId: item.Id), CancellationToken.None);

        Assert.NotNull(result.Value);

        // One presence for this item on this account, carrying the current external id.
        ChannelListing channelListing = await db.ChannelListings.SingleAsync(candidate => candidate.InventoryItemId == item.Id);
        Assert.Equal("EB-NEW", channelListing.ExternalListingId);
    }

    [Fact]
    public async Task Reconcile_link_existing_attaches_a_channel_listing_without_creating_an_item()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_link_existing_attaches_a_channel_listing_without_creating_an_item));
        await using HaulScoutDbContext _ = db;
        InventoryItem item = new() { OrganizationId = org, Title = "My Camera", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-3", "Vintage Camera", 120m)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();

        ActionResult<PlatformListingReconcileResultDto> result = await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(InventoryItemId: item.Id), CancellationToken.None);

        Assert.False(result.Value!.CreatedInventoryItem);
        Assert.Equal(1, await db.InventoryItems.CountAsync()); // no new item
        ChannelListing channel = await db.ChannelListings.SingleAsync();
        Assert.Equal(item.Id, channel.InventoryItemId);
    }

    [Fact]
    public async Task Unlink_reverts_to_pending_and_removes_channel_listing_created_by_reconcile()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Unlink_reverts_to_pending_and_removes_channel_listing_created_by_reconcile));
        await using HaulScoutDbContext _ = db;
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-UNLINK", "Vintage Camera", 120m)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();
        await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(CreateNewItem: true), CancellationToken.None);

        ActionResult<PlatformListingDto> result = await controller.Unlink(listing.Id, CancellationToken.None);

        PlatformListingDto dto = result.Value!;
        Assert.Equal(PlatformListingReconcileStatus.Pending, dto.ReconcileStatus);
        Assert.Null(dto.ReconciledInventoryItemId);
        PlatformListing stored = await db.PlatformListings.SingleAsync();
        Assert.False(stored.CreatedChannelListingOnReconcile);
        Assert.Null(stored.ReconciledChannelListingId);
        Assert.Equal(0, await db.ChannelListings.CountAsync());
    }

    [Fact]
    public async Task Unlink_preserves_channel_listing_that_existed_before_reconcile()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Unlink_preserves_channel_listing_that_existed_before_reconcile));
        await using HaulScoutDbContext _ = db;
        InventoryItem first = new() { OrganizationId = org, Title = "First Camera", Status = InventoryStatus.ReadyToList };
        InventoryItem second = new() { OrganizationId = org, Title = "Second Camera", Status = InventoryStatus.ReadyToList };
        ChannelAccount account = new() { OrganizationId = org, Channel = "eBay", DisplayName = "eBay", ConnectedAt = DateTimeOffset.UtcNow };
        db.InventoryItems.AddRange(first, second);
        db.ChannelAccounts.Add(account);
        await db.SaveChangesAsync();
        db.ChannelListings.Add(new ChannelListing
        {
            OrganizationId = org,
            InventoryItemId = first.Id,
            ChannelAccountId = account.Id,
            ExternalListingId = "EB-PREEXISTING",
        });
        await db.SaveChangesAsync();

        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-PREEXISTING", "Vintage Camera", 120m)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();
        await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(InventoryItemId: second.Id), CancellationToken.None);

        await controller.Unlink(listing.Id, CancellationToken.None);

        ChannelListing channel = await db.ChannelListings.SingleAsync();
        Assert.Equal(second.Id, channel.InventoryItemId); // unlink removes only the import reconciliation, not an existing channel record.
        Assert.Equal(PlatformListingReconcileStatus.Pending, (await db.PlatformListings.SingleAsync()).ReconcileStatus);
    }

    [Fact]
    public async Task Reconcile_again_moves_the_channel_listing_to_the_new_inventory_item()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_again_moves_the_channel_listing_to_the_new_inventory_item));
        await using HaulScoutDbContext _ = db;
        InventoryItem first = new() { OrganizationId = org, Title = "First Camera", Status = InventoryStatus.ReadyToList };
        InventoryItem second = new() { OrganizationId = org, Title = "Second Camera", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.AddRange(first, second);
        await db.SaveChangesAsync();
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-RELINK", "Vintage Camera", 120m)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();
        await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(InventoryItemId: first.Id), CancellationToken.None);

        await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(InventoryItemId: second.Id), CancellationToken.None);

        ChannelListing channel = await db.ChannelListings.SingleAsync();
        Assert.Equal(second.Id, channel.InventoryItemId);
        PlatformListing linked = await db.PlatformListings.SingleAsync();
        Assert.Equal(second.Id, linked.ReconciledInventoryItemId);
        Assert.True(linked.CreatedChannelListingOnReconcile);
    }

    [Fact]
    public async Task Reconcile_requires_a_target()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_requires_a_target));
        await using HaulScoutDbContext _ = db;
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-4", "Thing", 10m)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();

        ActionResult<PlatformListingReconcileResultDto> result = await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(), CancellationToken.None);
        ProblemAssert.Invalid(result.Result);
    }

    [Fact]
    public async Task List_suggests_a_match_by_title_and_flags_already_sold()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(List_suggests_a_match_by_title_and_flags_already_sold));
        await using HaulScoutDbContext _ = db;
        InventoryItem item = new() { OrganizationId = org, Title = "Levi's 501 Denim Jacket", Status = InventoryStatus.Sold };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay",
            [Listing("EB-5", "Vintage Levi's 501 Denim Jacket Men's Medium", 65m)]), CancellationToken.None);

        ActionResult<IReadOnlyList<PlatformListingDto>> listings = await controller.List(null, null, CancellationToken.None);
        PlatformListingDto dto = Assert.Single(listings.Value!);
        Assert.Equal(item.Id, dto.SuggestedInventoryItemId);
        Assert.True(dto.SuggestedInventoryItemAlreadySold);
    }

    [Fact]
    public async Task Ignored_listings_refresh_on_reingest_but_stay_ignored_until_restored()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Ignored_listings_refresh_on_reingest_but_stay_ignored_until_restored));
        await using HaulScoutDbContext _ = db;
        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-6", "Thing", 10m)]), CancellationToken.None);
        PlatformListing listing = await db.PlatformListings.SingleAsync();
        await controller.Ignore(listing.Id, CancellationToken.None);

        ActionResult<IngestPlatformListingsResultDto> reingest = await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-6", "Thing", 999m)]), CancellationToken.None);
        Assert.Equal(0, reingest.Value!.Skipped);
        Assert.Equal(1, reingest.Value!.Updated);
        PlatformListing refreshed = await db.PlatformListings.SingleAsync();
        Assert.Equal(999m, refreshed.Price);
        Assert.Equal(PlatformListingReconcileStatus.Ignored, refreshed.ReconcileStatus);

        await controller.Unlink(refreshed.Id, CancellationToken.None);
        Assert.Equal(PlatformListingReconcileStatus.Pending, (await db.PlatformListings.SingleAsync()).ReconcileStatus);
    }
}
