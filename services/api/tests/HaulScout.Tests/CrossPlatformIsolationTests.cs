using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Marketplaces number their own orders and listings, and nothing stops two of them using the same number. Every
/// place that looks something up by one of those numbers therefore has to say which marketplace it means — or a
/// Vinted sale settles against the item behind eBay's listing of that number, which is real money posted to the
/// wrong item.
/// </summary>
public sealed class CrossPlatformIsolationTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public required HaulScoutDbContext Db { get; init; }
        public required Guid Org { get; init; }
        public required PlatformOrderIngestionService Ingestion { get; init; }
        public required PlatformOrderSuggestionService Suggestions { get; init; }
    }

    private static Harness NewHarness()
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(
            new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();

        StubCurrentUser currentUser = new(org, user);
        OrderAssembler assembler = new(db, currentUser);
        EfPlatformOrderStore orderStore = new(db, assembler);
        PlatformOrderSuggestionService suggestions = new(new EfPlatformOrderSuggestionStore(db, currentUser));
        PlatformOrderWorkflowService workflow = new(orderStore, currentUser, suggestions);
        PlatformOrderAutoMatchService autoMatch = new(suggestions, workflow, orderStore, currentUser);
        return new Harness
        {
            Db = db,
            Org = org,
            Ingestion = new PlatformOrderIngestionService(orderStore, currentUser, suggestions, workflow, autoMatch),
            Suggestions = suggestions,
        };
    }

    private static IngestPlatformOrderInput Order(string platform, string externalOrderId, string externalItemId) => new(
        ExternalOrderId: externalOrderId,
        OrderedAt: new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
        ItemsSubtotal: 20m,
        ShippingCharged: 0m,
        TaxCollected: 0m,
        FeeTotal: 0m,
        Lines: [new IngestPlatformOrderLineInput("Plaid Shirt", 20m, ExternalItemId: externalItemId)],
        Platform: platform);

    [Fact]
    public async Task The_same_order_number_on_two_marketplaces_is_two_orders()
    {
        Harness harness = NewHarness();
        await using HaulScoutDbContext _ = harness.Db;

        await harness.Ingestion.IngestAsync("eBay", [Order("eBay", "55501", "item-a")], null, CancellationToken.None);
        await harness.Ingestion.IngestAsync("Vinted", [Order("Vinted", "55501", "item-b")], null, CancellationToken.None);

        List<PlatformOrder> orders = await harness.Db.PlatformOrders.ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equivalent(new[] { "eBay", "Vinted" }, orders.Select(order => order.Platform).ToArray());
        Assert.All(orders, order => Assert.Equal("55501", order.ExternalOrderId));

        // Re-reading one marketplace's order leaves the other's alone.
        (IngestPlatformOrdersResultDto? again, string? error) =
            await harness.Ingestion.IngestAsync("eBay", [Order("eBay", "55501", "item-a")], null, CancellationToken.None);
        Assert.Null(error);
        Assert.Equal(0, again!.Created);
        Assert.Equal(2, await harness.Db.PlatformOrders.CountAsync());
    }

    [Fact]
    public async Task A_cancellation_on_one_marketplace_leaves_the_others_order_alone()
    {
        Harness harness = NewHarness();
        await using HaulScoutDbContext _ = harness.Db;
        await harness.Ingestion.IngestAsync("eBay", [Order("eBay", "55501", "item-a")], null, CancellationToken.None);
        await harness.Ingestion.IngestAsync("Vinted", [Order("Vinted", "55501", "item-b")], null, CancellationToken.None);

        PlatformOrderCancellations applied = await harness.Ingestion.ApplyCancellationsAsync("Vinted", ["55501"], CancellationToken.None);

        Assert.Equal(1, applied.SetAside);
        Assert.Equal(PlatformOrderStatus.Ignored, (await harness.Db.PlatformOrders.SingleAsync(order => order.Platform == "Vinted")).Status);
        Assert.Equal(PlatformOrderStatus.Imported, (await harness.Db.PlatformOrders.SingleAsync(order => order.Platform == "eBay")).Status);
    }

    [Fact]
    public async Task A_cross_posted_item_is_matched_by_each_marketplaces_own_number()
    {
        Harness harness = NewHarness();
        await using HaulScoutDbContext _ = harness.Db;

        // The ordinary case here: one item, listed on both marketplaces, at different prices.
        InventoryItem boots = new() { OrganizationId = harness.Org, Title = "Leather Boots Size 9" };
        ChannelAccount ebay = new() { OrganizationId = harness.Org, Channel = "eBay", DisplayName = "Overhaul eBay" };
        ChannelAccount vinted = new() { OrganizationId = harness.Org, Channel = "Vinted", DisplayName = "Overhaul Vinted" };
        harness.Db.InventoryItems.Add(boots);
        harness.Db.ChannelAccounts.AddRange(ebay, vinted);
        harness.Db.ChannelListings.AddRange(
            new ChannelListing { OrganizationId = harness.Org, InventoryItemId = boots.Id, ChannelAccountId = ebay.Id, ExternalListingId = "E-55501", ListedPrice = 48m },
            new ChannelListing { OrganizationId = harness.Org, InventoryItemId = boots.Id, ChannelAccountId = vinted.Id, ExternalListingId = "V-98800", ListedPrice = 42m });
        await harness.Db.SaveChangesAsync();

        // Whichever marketplace it sells on, the sale finds the same item by that marketplace's number.
        await harness.Ingestion.IngestAsync("Vinted", [Order("Vinted", "V-1", "V-98800")], null, CancellationToken.None);
        PlatformOrder sold = await harness.Db.PlatformOrders.Include(order => order.Lines).SingleAsync();
        Assert.Equal(boots.Id, Assert.Single(sold.Lines).InventoryItemId);
    }

    [Fact]
    public async Task An_item_number_from_one_marketplace_never_matches_anothers_listing()
    {
        Harness harness = NewHarness();
        await using HaulScoutDbContext _ = harness.Db;

        // An eBay listing numbered 55501, linked to a boot.
        InventoryItem boots = new() { OrganizationId = harness.Org, Title = "Leather Boots Size 9" };
        ChannelAccount ebay = new() { OrganizationId = harness.Org, Channel = "eBay", DisplayName = "Overhaul eBay" };
        harness.Db.InventoryItems.Add(boots);
        harness.Db.ChannelAccounts.Add(ebay);
        harness.Db.ChannelListings.Add(new ChannelListing
        {
            OrganizationId = harness.Org,
            InventoryItemId = boots.Id,
            ChannelAccountId = ebay.Id,
            ExternalListingId = "55501",
        });
        await harness.Db.SaveChangesAsync();

        // A Vinted sale of Vinted's item 55501 — a different thing that happens to share the digits.
        await harness.Ingestion.IngestAsync("Vinted", [Order("Vinted", "V-1", "55501")], null, CancellationToken.None);
        PlatformOrder vintedOrder = await harness.Db.PlatformOrders.Include(order => order.Lines).SingleAsync(order => order.Platform == "Vinted");

        // It is left for a person: unmatched, unsettled, and the boot is not sold.
        Assert.Null(Assert.Single(vintedOrder.Lines).InventoryItemId);
        Assert.Equal(PlatformOrderStatus.Imported, vintedOrder.Status);
        Dictionary<Guid, PlatformLineMatch> matches = await harness.Suggestions.BuildMatchesAsync([vintedOrder], CancellationToken.None);
        Assert.DoesNotContain(matches, entry => entry.Value.Source == PlatformLineMatchSource.PlatformItemId);
        Assert.False(InventoryLifecycle.IsSold((await harness.Db.InventoryItems.SingleAsync()).Status));

        // The same number on eBay does match, because there it really is that listing.
        await harness.Ingestion.IngestAsync("eBay", [Order("eBay", "E-1", "55501")], null, CancellationToken.None);
        PlatformOrder ebayOrder = await harness.Db.PlatformOrders.Include(order => order.Lines).SingleAsync(order => order.Platform == "eBay");
        Assert.Equal(boots.Id, Assert.Single(ebayOrder.Lines).InventoryItemId);
    }
}
