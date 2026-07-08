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

public class PlatformOrderIngestionTests
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
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static InventoryItem SeedItem(HaulScoutDbContext db, Guid org, string title, decimal cost)
    {
        var item = new InventoryItem { OrganizationId = org, Title = title, PurchasePrice = cost, Status = InventoryStatus.Listed };
        db.InventoryItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static readonly DateTimeOffset OrderedAt = DateTimeOffset.UtcNow.AddDays(-3);

    private static IngestPlatformOrderInput Order(
        string externalId,
        IReadOnlyList<IngestPlatformOrderLineInput> lines,
        decimal shipping = 0,
        decimal tax = 0,
        decimal fee = 0,
        decimal? payout = null,
        bool taxRemitted = true)
    {
        decimal subtotal = lines.Sum(line => line.UnitPrice * line.Quantity);
        return new IngestPlatformOrderInput(
            ExternalOrderId: externalId,
            OrderedAt: OrderedAt,
            ItemsSubtotal: subtotal,
            ShippingCharged: shipping,
            TaxCollected: tax,
            FeeTotal: fee,
            Lines: lines,
            PayoutAmount: payout,
            TaxRemittedByPlatform: taxRemitted);
    }

    [Fact]
    public async Task Ingest_creates_orders_then_reingest_updates_in_place()
    {
        var (db, org, user) = NewContext(nameof(Ingest_creates_orders_then_reingest_updates_in_place));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));

        var lines = new[] { new IngestPlatformOrderLineInput("Vintage Denim Jacket", 80m) };
        var first = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-1", lines, fee: 10m)]), CancellationToken.None);
        Assert.Equal(1, first.Value!.Created);
        Assert.Equal(0, first.Value.Updated);

        // Re-ingest the same order id with a corrected fee: updates in place, no duplicate row.
        var second = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-1", lines, fee: 13.45m)]), CancellationToken.None);
        Assert.Equal(0, second.Value!.Created);
        Assert.Equal(1, second.Value.Updated);

        var order = Assert.Single(await db.PlatformOrders.Include(o => o.Lines).ToListAsync());
        Assert.Equal(13.45m, order.FeeTotal);
        Assert.Single(order.Lines);
    }

    [Fact]
    public async Task Reingesting_a_reconciled_order_is_skipped()
    {
        var (db, org, user) = NewContext(nameof(Reingesting_a_reconciled_order_is_skipped));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));
        var item = SeedItem(db, org, "Leather Boots", 30m);

        var lines = new[] { new IngestPlatformOrderLineInput("Leather Boots", 90m) };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-9", lines, fee: 12m)]), CancellationToken.None);
        var order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id, new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        var reingest = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-9", lines, fee: 999m)]), CancellationToken.None);
        Assert.Equal(1, reingest.Value!.Skipped);

        var unchanged = await db.PlatformOrders.SingleAsync();
        Assert.Equal(12m, unchanged.FeeTotal); // reconciled numbers are posted; never overwritten
        Assert.Equal(PlatformOrderStatus.Reconciled, unchanged.Status);
    }

    [Fact]
    public async Task Reconcile_single_line_pushes_exact_platform_figures_into_the_sale()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_single_line_pushes_exact_platform_figures_into_the_sale));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        var lines = new[] { new IngestPlatformOrderLineInput("Brass Lamp", 120m) };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-2", lines, shipping: 12m, tax: 9.90m, fee: 19.40m)]), CancellationToken.None);
        var order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();

        var result = await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        var reconcile = result.Value!;
        var sale = Assert.Single(reconcile.Sales);
        Assert.Equal(120m, sale.SoldPrice);
        Assert.Equal(12m, sale.ShippingCharged);
        Assert.Equal(19.40m, sale.PlatformFees);   // exact platform fee, not hand-entered
        Assert.Equal(9.90m, sale.SalesTaxCollected);
        Assert.Equal(40m, sale.CostOfGoods);
        Assert.Equal(120m + 12m - 40m - 19.40m, sale.NetProfit); // buyer shipping is revenue; tax excluded

        var stored = await db.PlatformOrders.SingleAsync();
        Assert.Equal(PlatformOrderStatus.Reconciled, stored.Status);
        Assert.Equal(reconcile.SaleGroupId, stored.ReconciledSaleGroupId);
        Assert.NotNull(stored.ReconciledAt);

        var soldItem = await db.InventoryItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(InventoryStatus.Sold, soldItem.Status);
        Assert.Equal(OrderedAt, soldItem.SoldAt);
    }

    [Fact]
    public async Task Reconcile_multi_line_allocates_shared_costs_and_sums_back_to_the_order()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_multi_line_allocates_shared_costs_and_sums_back_to_the_order));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));
        var a = SeedItem(db, org, "Item A", 10m);
        var b = SeedItem(db, org, "Item B", 20m);

        var lines = new[]
        {
            new IngestPlatformOrderLineInput("Item A", 60m, ExternalItemId: "EB-A"),
            new IngestPlatformOrderLineInput("Item B", 40m, ExternalItemId: "EB-B"),
        };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-3", lines, shipping: 10m, tax: 8m, fee: 15m)]), CancellationToken.None);
        var order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        var lineA = order.Lines.Single(line => line.Title == "Item A");
        var lineB = order.Lines.Single(line => line.Title == "Item B");

        var result = await controller.Reconcile(order.Id, new ReconcilePlatformOrderRequest(
        [
            new ReconcilePlatformOrderLineMap(lineA.Id, a.Id),
            new ReconcilePlatformOrderLineMap(lineB.Id, b.Id),
        ]), CancellationToken.None);

        var sales = result.Value!.Sales;
        Assert.Equal(2, sales.Count);
        Assert.All(sales, sale => Assert.Equal(result.Value.SaleGroupId, sale.SaleGroupId));
        Assert.All(sales, sale => Assert.Equal("ORD-3", sale.GroupOrderReference));
        Assert.Equal(100m, sales.Sum(sale => sale.SoldPrice));         // exact per-item prices preserved
        Assert.Equal(15m, sales.Sum(sale => sale.PlatformFees));       // fee total fully allocated
        Assert.Equal(10m, sales.Sum(sale => sale.ShippingCharged));    // buyer shipping fully allocated
        Assert.Equal(8m, sales.Sum(sale => sale.SalesTaxCollected));   // tax fully allocated

        Assert.Equal(InventoryStatus.Sold, (await db.InventoryItems.SingleAsync(i => i.Id == a.Id)).Status);
        Assert.Equal(InventoryStatus.Sold, (await db.InventoryItems.SingleAsync(i => i.Id == b.Id)).Status);
    }

    [Fact]
    public async Task Reconcile_requires_every_line_to_be_mapped()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_requires_every_line_to_be_mapped));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));
        var a = SeedItem(db, org, "Item A", 10m);

        var lines = new[]
        {
            new IngestPlatformOrderLineInput("Item A", 60m),
            new IngestPlatformOrderLineInput("Item B", 40m),
        };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-4", lines)]), CancellationToken.None);
        var order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        var lineA = order.Lines.First(line => line.Title == "Item A");

        var result = await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(lineA.Id, a.Id)]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Equal(PlatformOrderStatus.Imported, (await db.PlatformOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reconcile_blocks_an_item_that_already_sold()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_blocks_an_item_that_already_sold));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));
        var item = SeedItem(db, org, "Brass Lamp", 40m);
        db.Sales.Add(new Sale
        {
            OrganizationId = org, InventoryItemId = item.Id, Platform = "eBay",
            SoldPrice = 100m, CostOfGoods = 40m, NetProfit = 60m, Status = SaleStatus.Recorded,
        });
        await db.SaveChangesAsync();

        var lines = new[] { new IngestPlatformOrderLineInput("Brass Lamp", 120m) };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-5", lines)]), CancellationToken.None);
        var order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();

        var result = await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Ingest_rejects_negative_money()
    {
        var (db, org, user) = NewContext(nameof(Ingest_rejects_negative_money));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));

        var lines = new[] { new IngestPlatformOrderLineInput("Item", 10m) };
        var result = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-6", lines, fee: -1m)]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.PlatformOrders.ToListAsync());
    }

    [Fact]
    public async Task Get_suggests_an_inventory_match_from_the_channel_listing_external_id()
    {
        var (db, org, user) = NewContext(nameof(Get_suggests_an_inventory_match_from_the_channel_listing_external_id));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));
        var item = SeedItem(db, org, "Signed Poster", 15m);
        var account = new ChannelAccount { OrganizationId = org, Channel = "eBay", DisplayName = "eBay" };
        db.ChannelAccounts.Add(account);
        db.ChannelListings.Add(new ChannelListing
        {
            OrganizationId = org, InventoryItemId = item.Id, ChannelAccountId = account.Id,
            Status = ChannelListingStatus.Active, ExternalListingId = "EBAY-ITEM-777",
        });
        await db.SaveChangesAsync();

        var lines = new[] { new IngestPlatformOrderLineInput("Signed Poster", 55m, ExternalItemId: "EBAY-ITEM-777") };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-7", lines)]), CancellationToken.None);
        var order = await db.PlatformOrders.SingleAsync();

        var detail = await controller.Get(order.Id, CancellationToken.None);
        var line = Assert.Single(detail.Value!.Lines);
        Assert.Equal(item.Id, line.SuggestedInventoryItemId);
        Assert.Equal("Signed Poster", line.SuggestedInventoryItemTitle);
    }

    [Fact]
    public async Task Payout_variance_flags_a_mismatch_between_expected_and_scraped_payout()
    {
        var (db, org, user) = NewContext(nameof(Payout_variance_flags_a_mismatch_between_expected_and_scraped_payout));
        await using var _ = db;
        var controller = new PlatformOrdersController(db, new StubCurrentUser(org, user));

        // Expected payout = 100 + 10 shipping - 18 fee = 92; platform actually deposited 90 → variance -2.
        var lines = new[] { new IngestPlatformOrderLineInput("Item", 100m) };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-8", lines, shipping: 10m, fee: 18m, payout: 90m)]), CancellationToken.None);
        var order = await db.PlatformOrders.SingleAsync();

        var detail = await controller.Get(order.Id, CancellationToken.None);
        Assert.Equal(92m, detail.Value!.ExpectedPayout);
        Assert.Equal(-2m, detail.Value.PayoutVariance);
    }
}
