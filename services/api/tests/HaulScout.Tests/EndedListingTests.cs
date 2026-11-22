using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Sync.Agents;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>A scan that read every live listing also says which listings are gone, and the books follow.</summary>
public sealed class EndedListingTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => Guid.Empty;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required HaulScoutDbContext Db { get; init; }
        public required Guid Org { get; init; }
        public required ChannelAccount Account { get; init; }
        public required PlatformListingImportService Service { get; init; }

        public async Task Read(params (string Id, decimal Price)[] listings)
        {
            await Service.IngestAsync(
                new IngestPlatformListingsRequest("eBay", listings.Select(l => new IngestPlatformListingInput(l.Id, $"Item {l.Id}", l.Price, PlatformListingStatus.Active)).ToList(), Account.Id),
                CancellationToken.None);
        }

        /// <summary>Time passes, so what the next scan reads is told apart from what the last one did.</summary>
        public static async Task<DateTimeOffset> NextScan()
        {
            await Task.Delay(15);
            DateTimeOffset since = DateTimeOffset.UtcNow;
            await Task.Delay(15);
            return since;
        }

        public Task<PlatformListing> Listing(string id) => Db.PlatformListings.SingleAsync(listing => listing.ExternalListingId == id);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static Harness NewHarness()
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        ChannelAccount account = new() { OrganizationId = org, Channel = "eBay", DisplayName = "Overhaul eBay" };
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul" });
        db.ChannelAccounts.Add(account);
        db.SaveChanges();
        return new Harness { Db = db, Org = org, Account = account, Service = new PlatformListingImportService(db, new StubCurrentUser(org)) };
    }

    [Fact]
    public async Task A_listing_the_scan_did_not_see_is_ended_and_the_ones_it_saw_stay_live()
    {
        await using Harness harness = NewHarness();
        await harness.Read(("A", 10m), ("B", 20m), ("C", 30m));
        DateTimeOffset since = await Harness.NextScan();

        await harness.Read(("A", 10m));
        ListingEndings endings = await harness.Service.EndUnseenAsync("eBay", harness.Account.Id, since, CancellationToken.None);

        Assert.Equal(new ListingEndings(2, 0), endings);
        Assert.Equal(PlatformListingStatus.Active, (await harness.Listing("A")).Status);
        Assert.Equal(PlatformListingStatus.Ended, (await harness.Listing("B")).Status);
        Assert.Equal(PlatformListingStatus.Ended, (await harness.Listing("C")).Status);
    }

    [Fact]
    public async Task A_listing_with_a_sale_on_file_is_sold_not_ended()
    {
        await using Harness harness = NewHarness();
        await harness.Read(("A", 10m), ("B", 20m));
        harness.Db.PlatformOrders.Add(new PlatformOrder
        {
            OrganizationId = harness.Org,
            Platform = "eBay",
            ExternalOrderId = "26-1",
            Lines = [new PlatformOrderLine { OrganizationId = harness.Org, Title = "Item B", ExternalItemId = "B" }],
        });
        await harness.Db.SaveChangesAsync();
        DateTimeOffset since = await Harness.NextScan();

        await harness.Read(("A", 10m));
        ListingEndings endings = await harness.Service.EndUnseenAsync("eBay", harness.Account.Id, since, CancellationToken.None);

        Assert.Equal(new ListingEndings(0, 1), endings);
        PlatformListing sold = await harness.Listing("B");
        Assert.Equal(PlatformListingStatus.Sold, sold.Status);
        Assert.NotNull(sold.MarkedSoldAt);
    }

    [Fact]
    public async Task A_scan_that_read_nothing_ends_nothing()
    {
        await using Harness harness = NewHarness();
        await harness.Read(("A", 10m), ("B", 20m));
        DateTimeOffset since = await Harness.NextScan();

        ListingEndings endings = await harness.Service.EndUnseenAsync("eBay", harness.Account.Id, since, CancellationToken.None);

        Assert.Equal(0, endings.Total);
        Assert.NotNull(endings.Held);
        Assert.All(await harness.Db.PlatformListings.ToListAsync(), listing => Assert.Equal(PlatformListingStatus.Active, listing.Status));
    }

    [Fact]
    public async Task Another_accounts_listings_are_left_alone()
    {
        await using Harness harness = NewHarness();
        ChannelAccount other = new() { OrganizationId = harness.Org, Channel = "eBay", DisplayName = "Second eBay" };
        harness.Db.ChannelAccounts.Add(other);
        await harness.Db.SaveChangesAsync();
        await harness.Read(("A", 10m));
        await harness.Service.IngestAsync(
            new IngestPlatformListingsRequest("eBay", [new IngestPlatformListingInput("Z", "Item Z", 5m, PlatformListingStatus.Active)], other.Id), CancellationToken.None);
        DateTimeOffset since = await Harness.NextScan();

        await harness.Read(("A", 10m));
        await harness.Service.EndUnseenAsync("eBay", harness.Account.Id, since, CancellationToken.None);

        Assert.Equal(PlatformListingStatus.Active, (await harness.Listing("Z")).Status);
    }

    [Fact]
    public async Task An_ended_listing_delists_its_presence_on_the_channel_and_a_returning_one_restores_it()
    {
        await using Harness harness = NewHarness();
        await harness.Read(("A", 10m), ("B", 20m));
        PlatformListing listing = await harness.Listing("B");
        InventoryItem item = new() { OrganizationId = harness.Org, Title = "Item B" };
        ChannelListing presence = new()
        {
            OrganizationId = harness.Org,
            InventoryItemId = item.Id,
            ChannelAccountId = harness.Account.Id,
            ExternalListingId = "B",
            Status = ChannelListingStatus.Active,
            ListedPrice = 20m,
        };
        harness.Db.InventoryItems.Add(item);
        harness.Db.ChannelListings.Add(presence);
        listing.ReconciledChannelListingId = presence.Id;
        listing.ReconciledInventoryItemId = item.Id;
        listing.ReconcileStatus = PlatformListingReconcileStatus.Linked;
        await harness.Db.SaveChangesAsync();
        DateTimeOffset since = await Harness.NextScan();

        await harness.Read(("A", 10m));
        await harness.Service.EndUnseenAsync("eBay", harness.Account.Id, since, CancellationToken.None);
        Assert.Equal(ChannelListingStatus.Delisted, (await harness.Db.ChannelListings.SingleAsync()).Status);

        // The seller relists it at a new price; the next scan reads it as live again and so is the presence.
        await harness.Read(("A", 10m), ("B", 18m));
        ChannelListing back = await harness.Db.ChannelListings.SingleAsync();
        Assert.Equal(ChannelListingStatus.Active, back.Status);
        Assert.Equal(18m, back.ListedPrice);
    }

    [Fact]
    public async Task A_price_changed_on_the_marketplace_reaches_the_linked_presence_and_a_pending_delist_stays_pending()
    {
        await using Harness harness = NewHarness();
        await harness.Read(("A", 10m));
        PlatformListing listing = await harness.Listing("A");
        InventoryItem item = new() { OrganizationId = harness.Org, Title = "Item A" };
        ChannelListing presence = new()
        {
            OrganizationId = harness.Org,
            InventoryItemId = item.Id,
            ChannelAccountId = harness.Account.Id,
            ExternalListingId = "A",
            Status = ChannelListingStatus.PendingDelist,
            ListedPrice = 10m,
        };
        harness.Db.InventoryItems.Add(item);
        harness.Db.ChannelListings.Add(presence);
        listing.ReconciledChannelListingId = presence.Id;
        listing.ReconciledInventoryItemId = item.Id;
        await harness.Db.SaveChangesAsync();

        await harness.Read(("A", 12.5m));

        ChannelListing after = await harness.Db.ChannelListings.SingleAsync();
        Assert.Equal(12.5m, after.ListedPrice);
        Assert.Equal(ChannelListingStatus.PendingDelist, after.Status);
    }
}
