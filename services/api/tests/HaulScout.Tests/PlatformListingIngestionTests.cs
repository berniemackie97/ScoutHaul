using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

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
        var org = Guid.NewGuid();
        var user = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        var tenant = new TenantContext();
        tenant.SetOrganization(org);
        var db = new HaulScoutDbContext(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static IngestPlatformListingInput Listing(string id, string title, decimal price, PlatformListingStatus status = PlatformListingStatus.Active) =>
        new(id, title, price, status);

    [Fact]
    public async Task Ingest_creates_then_reingest_updates_in_place()
    {
        var (db, org, user) = NewContext(nameof(Ingest_creates_then_reingest_updates_in_place));
        await using var _ = db;
        var controller = new PlatformListingsController(db, new StubCurrentUser(org, user));

        var first = await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 40m)]), CancellationToken.None);
        Assert.Equal(1, first.Value!.Created);

        var second = await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-1", "Denim Jacket", 45m)]), CancellationToken.None);
        Assert.Equal(0, second.Value!.Created);
        Assert.Equal(1, second.Value.Updated);

        var listing = Assert.Single(await db.PlatformListings.ToListAsync());
        Assert.Equal(45m, listing.Price);
    }

    [Fact]
    public async Task Reconcile_create_new_seeds_a_draft_item_and_a_channel_listing()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_create_new_seeds_a_draft_item_and_a_channel_listing));
        await using var _ = db;
        var controller = new PlatformListingsController(db, new StubCurrentUser(org, user));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-2", "Vintage Camera", 120m)]), CancellationToken.None);
        var listing = await db.PlatformListings.SingleAsync();

        var result = await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(CreateNewItem: true), CancellationToken.None);

        var reconcile = result.Value!;
        Assert.True(reconcile.CreatedInventoryItem);
        var item = await db.InventoryItems.SingleAsync(i => i.Id == reconcile.InventoryItemId);
        Assert.Equal("Vintage Camera", item.Title);
        Assert.Equal(InventoryStatus.Listed, item.Status);      // active listing → listed
        Assert.Equal(120m, item.EstimatedSalePrice);

        var channel = await db.ChannelListings.SingleAsync(c => c.Id == reconcile.ChannelListingId);
        Assert.Equal("EB-2", channel.ExternalListingId);
        Assert.Equal(ChannelListingStatus.Active, channel.Status);

        var linked = await db.PlatformListings.SingleAsync();
        Assert.Equal(PlatformListingReconcileStatus.Linked, linked.ReconcileStatus);
        Assert.Equal(item.Id, linked.ReconciledInventoryItemId);
    }

    [Fact]
    public async Task Reconcile_link_existing_attaches_a_channel_listing_without_creating_an_item()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_link_existing_attaches_a_channel_listing_without_creating_an_item));
        await using var _ = db;
        var item = new InventoryItem { OrganizationId = org, Title = "My Camera", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        var controller = new PlatformListingsController(db, new StubCurrentUser(org, user));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-3", "Vintage Camera", 120m)]), CancellationToken.None);
        var listing = await db.PlatformListings.SingleAsync();

        var result = await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(InventoryItemId: item.Id), CancellationToken.None);

        Assert.False(result.Value!.CreatedInventoryItem);
        Assert.Equal(1, await db.InventoryItems.CountAsync()); // no new item
        var channel = await db.ChannelListings.SingleAsync();
        Assert.Equal(item.Id, channel.InventoryItemId);
    }

    [Fact]
    public async Task Reconcile_requires_a_target()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_requires_a_target));
        await using var _ = db;
        var controller = new PlatformListingsController(db, new StubCurrentUser(org, user));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-4", "Thing", 10m)]), CancellationToken.None);
        var listing = await db.PlatformListings.SingleAsync();

        var result = await controller.Reconcile(listing.Id, new ReconcilePlatformListingRequest(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task List_suggests_a_match_by_title_and_flags_already_sold()
    {
        var (db, org, user) = NewContext(nameof(List_suggests_a_match_by_title_and_flags_already_sold));
        await using var _ = db;
        var item = new InventoryItem { OrganizationId = org, Title = "Levi's 501 Denim Jacket", Status = InventoryStatus.Sold };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        var controller = new PlatformListingsController(db, new StubCurrentUser(org, user));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay",
            [Listing("EB-5", "Vintage Levi's 501 Denim Jacket Men's Medium", 65m)]), CancellationToken.None);

        var listings = await controller.List(null, null, CancellationToken.None);
        var dto = Assert.Single(listings.Value!);
        Assert.Equal(item.Id, dto.SuggestedInventoryItemId);
        Assert.True(dto.SuggestedInventoryItemAlreadySold);
    }

    [Fact]
    public async Task Ignored_listings_are_skipped_on_reingest()
    {
        var (db, org, user) = NewContext(nameof(Ignored_listings_are_skipped_on_reingest));
        await using var _ = db;
        var controller = new PlatformListingsController(db, new StubCurrentUser(org, user));
        await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-6", "Thing", 10m)]), CancellationToken.None);
        var listing = await db.PlatformListings.SingleAsync();
        await controller.Ignore(listing.Id, CancellationToken.None);

        var reingest = await controller.Ingest(new IngestPlatformListingsRequest("eBay", [Listing("EB-6", "Thing", 999m)]), CancellationToken.None);
        Assert.Equal(1, reingest.Value!.Skipped);
        Assert.Equal(10m, (await db.PlatformListings.SingleAsync()).Price); // untouched
    }
}
