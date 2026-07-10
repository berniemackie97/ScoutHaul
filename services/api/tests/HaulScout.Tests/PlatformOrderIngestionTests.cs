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
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, org, user);
    }


    private static PlatformOrdersController NewController(HaulScoutDbContext db, Guid org, Guid user)
    {
        StubCurrentUser currentUser = new(org, user);
        return new PlatformOrdersController(db, currentUser, new OrderAssembler(db, currentUser));
    }

    private static InventoryItem SeedItem(HaulScoutDbContext db, Guid org, string title, decimal cost)
    {
        InventoryItem item = new() { OrganizationId = org, Title = title, PurchasePrice = cost, Status = InventoryStatus.Listed };
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
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Ingest_creates_orders_then_reingest_updates_in_place));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);

        IngestPlatformOrderLineInput[] lines = [new("Vintage Denim Jacket", 80m)];
        ActionResult<IngestPlatformOrdersResultDto> first = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-1", lines, fee: 10m)]), CancellationToken.None);
        Assert.Equal(1, first.Value!.Created);
        Assert.Equal(0, first.Value.Updated);

        // Re-ingest the same order id with a corrected fee: updates in place, no duplicate row.
        ActionResult<IngestPlatformOrdersResultDto> second = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-1", lines, fee: 13.45m)]), CancellationToken.None);
        Assert.Equal(0, second.Value!.Created);
        Assert.Equal(1, second.Value.Updated);

        PlatformOrder order = Assert.Single(await db.PlatformOrders.Include(o => o.Lines).ToListAsync());
        Assert.Equal(13.45m, order.FeeTotal);
        Assert.Single(order.Lines);
    }

    [Fact]
    public async Task Reingesting_a_reconciled_order_is_skipped()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reingesting_a_reconciled_order_is_skipped));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem item = SeedItem(db, org, "Leather Boots", 30m);

        IngestPlatformOrderLineInput[] lines = [new("Leather Boots", 90m)];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-9", lines, fee: 12m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id, new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        ActionResult<IngestPlatformOrdersResultDto> reingest = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-9", lines, fee: 999m)]), CancellationToken.None);
        Assert.Equal(1, reingest.Value!.Skipped);

        PlatformOrder unchanged = await db.PlatformOrders.SingleAsync();
        Assert.Equal(12m, unchanged.FeeTotal); // reconciled numbers are posted; never overwritten
        Assert.Equal(PlatformOrderStatus.Reconciled, unchanged.Status);
    }

    [Fact]
    public async Task Reconcile_single_line_pushes_exact_platform_figures_into_the_sale()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_single_line_pushes_exact_platform_figures_into_the_sale));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem item = SeedItem(db, org, "Brass Lamp", 40m);

        IngestPlatformOrderLineInput[] lines = [new("Brass Lamp", 120m)];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-2", lines, shipping: 12m, tax: 9.90m, fee: 19.40m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();

        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        PlatformOrderReconcileResultDto reconcile = result.Value!;
        SaleDto sale = Assert.Single(reconcile.Sales);
        Assert.Equal(120m, sale.SoldPrice);
        Assert.Equal(12m, sale.ShippingCharged);
        Assert.Equal(19.40m, sale.PlatformFees);   // exact platform fee, not hand-entered
        Assert.Equal(9.90m, sale.SalesTaxCollected);
        Assert.Equal(40m, sale.CostOfGoods);
        Assert.Equal(120m + 12m - 40m - 19.40m, sale.NetProfit); // buyer shipping is revenue; tax excluded

        PlatformOrder stored = await db.PlatformOrders.SingleAsync();
        Assert.Equal(PlatformOrderStatus.Reconciled, stored.Status);
        Assert.Equal(reconcile.SaleGroupId, stored.ReconciledSaleGroupId);
        Assert.NotNull(stored.ReconciledAt);

        InventoryItem soldItem = await db.InventoryItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(InventoryStatus.Sold, soldItem.Status);
        Assert.Equal(OrderedAt, soldItem.SoldAt);
    }

    [Fact]
    public async Task Reconcile_multi_line_allocates_shared_costs_and_sums_back_to_the_order()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_multi_line_allocates_shared_costs_and_sums_back_to_the_order));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem a = SeedItem(db, org, "Item A", 10m);
        InventoryItem b = SeedItem(db, org, "Item B", 20m);

        IngestPlatformOrderLineInput[] lines =
        [
            new("Item A", 60m, ExternalItemId: "EB-A"),
            new("Item B", 40m, ExternalItemId: "EB-B")
        ];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-3", lines, shipping: 10m, tax: 8m, fee: 15m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        PlatformOrderLine lineA = order.Lines.Single(line => line.Title == "Item A");
        PlatformOrderLine lineB = order.Lines.Single(line => line.Title == "Item B");

        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(order.Id, new ReconcilePlatformOrderRequest(
        [
            new ReconcilePlatformOrderLineMap(lineA.Id, a.Id),
            new ReconcilePlatformOrderLineMap(lineB.Id, b.Id),
        ]), CancellationToken.None);

        IReadOnlyList<SaleDto> sales = result.Value!.Sales;
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
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_requires_every_line_to_be_mapped));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem a = SeedItem(db, org, "Item A", 10m);

        IngestPlatformOrderLineInput[] lines =
        [
            new("Item A", 60m),
            new("Item B", 40m)
        ];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-4", lines)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        PlatformOrderLine lineA = order.Lines.First(line => line.Title == "Item A");

        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(lineA.Id, a.Id)]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Equal(PlatformOrderStatus.Imported, (await db.PlatformOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reconcile_corrects_a_hand_recorded_sale_in_place_with_exact_fees()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_corrects_a_hand_recorded_sale_in_place_with_exact_fees));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem item = SeedItem(db, org, "Brass Lamp", 40m);
        item.Status = InventoryStatus.Sold;
        // A sale the operator hand-entered earlier with guessed fees + their own $8 shipping label cost.
        db.Sales.Add(new Sale
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 100m,
            CostOfGoods = 40m,
            PlatformFees = 12m,
            ShippingCost = 8m,
            NetProfit = 40m,
            Status = SaleStatus.Recorded,
        });
        await db.SaveChangesAsync();

        IngestPlatformOrderLineInput[] lines = [new("Brass Lamp", 120m)];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-5", lines, shipping: 10m, tax: 9m, fee: 19.40m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();

        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        PlatformOrderReconcileResultDto reconcile = result.Value!;
        Assert.Equal(0, reconcile.CreatedSaleCount);
        Assert.Equal(1, reconcile.CorrectedSaleCount);

        // Exactly one sale for the item — corrected, never duplicated.
        Sale sale = Assert.Single(await db.Sales.Where(s => s.InventoryItemId == item.Id).ToListAsync());
        Assert.Equal(120m, sale.SoldPrice);          // platform's exact figures overwrite the guesses
        Assert.Equal(10m, sale.ShippingCharged);
        Assert.Equal(19.40m, sale.PlatformFees);
        Assert.Equal(9m, sale.SalesTaxCollected);
        Assert.Equal(8m, sale.ShippingCost);         // seller's own label cost preserved
        Assert.Equal(120m + 10m - 40m - 19.40m - 8m, sale.NetProfit);

        SaleCorrection correction = Assert.Single(await db.SaleCorrections.Where(c => c.SaleId == sale.Id).ToListAsync());
        Assert.Equal(1, correction.Version);
        Assert.Contains("ORD-5", correction.Reason);
        Assert.Contains("40", correction.BeforeJson);   // prior net preserved in the audit
    }

    [Fact]
    public async Task Reconcile_mixes_a_new_sale_and_a_correction_in_one_order()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_mixes_a_new_sale_and_a_correction_in_one_order));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem fresh = SeedItem(db, org, "Fresh Item", 10m);
        InventoryItem recorded = SeedItem(db, org, "Recorded Item", 15m);
        recorded.Status = InventoryStatus.Sold;
        db.Sales.Add(new Sale
        {
            OrganizationId = org,
            InventoryItemId = recorded.Id,
            Platform = "eBay",
            SoldPrice = 50m,
            CostOfGoods = 15m,
            NetProfit = 35m,
            Status = SaleStatus.Recorded,
        });
        await db.SaveChangesAsync();

        IngestPlatformOrderLineInput[] lines =
        [
            new("Fresh Item", 60m),
            new("Recorded Item", 40m)
        ];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-10", lines, fee: 15m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        PlatformOrderLine freshLine = order.Lines.Single(line => line.Title == "Fresh Item");
        PlatformOrderLine recordedLine = order.Lines.Single(line => line.Title == "Recorded Item");

        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(order.Id, new ReconcilePlatformOrderRequest(
        [
            new ReconcilePlatformOrderLineMap(freshLine.Id, fresh.Id),
            new ReconcilePlatformOrderLineMap(recordedLine.Id, recorded.Id),
        ]), CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedSaleCount);
        Assert.Equal(1, result.Value.CorrectedSaleCount);
        // Still exactly one sale per item, all sharing the order's group.
        Assert.Equal(1, await db.Sales.CountAsync(s => s.InventoryItemId == fresh.Id));
        Assert.Equal(1, await db.Sales.CountAsync(s => s.InventoryItemId == recorded.Id));
        Assert.All(result.Value.Sales, sale => Assert.Equal(result.Value.SaleGroupId, sale.SaleGroupId));
        Assert.Equal(15m, result.Value.Sales.Sum(sale => sale.PlatformFees)); // full fee allocated across both
    }

    private const string EbayCsv = """
"Report for Apr 9, 2026 to Jul 7, 2026"
Listing title,eBay item ID,Store category L1,Store category L2,Quantity sold,Total sales (Includes taxes),Item sales,Taxes and government fees paid by buyer to you,Taxes and government fees paid by buyer to eBay,Shipping and handling paid by buyer to you,Total selling costs,Insertion fees,Optional listing upgrade fees,Final value fees,Promoted Listings - General fees,Promoted Listings - Priority fees,Ads Express fees,Promoted Offsite - Fees,International fees,Other eBay fees,Deposit processing fees,Fee credits,Shipping labels cost (Amount you paid to buy shipping labels on eBay),Net sales (Net of taxes and selling costs),Average Selling price,Quantity sold via promoted listing,Quantity sold via Best Offers,Quantity sold via Seller Initiated Offers,
Brass Lamp,377251318593,-,-,1,$88.12,$77.28,$0.00,$7.15,$3.69,$18.05,$0.00,$0.00,$19.29,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$7.21,$5.97,$62.92,$88.12,0,1,0
""";

    [Fact]
    public async Task Import_ebay_csv_ingests_then_reimports_idempotently()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Import_ebay_csv_ingests_then_reimports_idempotently));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);

        ActionResult<IngestPlatformOrdersResultDto> first = await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayCsv), CancellationToken.None);
        Assert.Equal(1, first.Value!.Created);

        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        Assert.Equal("377251318593", order.ExternalOrderId);
        Assert.Equal(12.08m, order.FeeTotal);            // exact eBay cut (18.05 − 5.97 label)
        Assert.Equal(5.97m, order.SellerShippingCost);

        ActionResult<IngestPlatformOrdersResultDto> second = await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayCsv), CancellationToken.None);
        Assert.Equal(0, second.Value!.Created);
        Assert.Equal(1, second.Value.Updated);
        Assert.Single(await db.PlatformOrders.ToListAsync());
    }

    [Fact]
    public async Task Reconcile_new_sale_from_csv_carries_the_shipping_label_cost()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_new_sale_from_csv_carries_the_shipping_label_cost));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();

        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        SaleDto sale = Assert.Single(result.Value!.Sales);
        Assert.Equal(77.28m, sale.SoldPrice);
        Assert.Equal(12.08m, sale.PlatformFees);
        Assert.Equal(5.97m, sale.ShippingCost);                         // seller label cost carried from the report
        Assert.Equal(77.28m + 3.69m - 40m - 12.08m - 5.97m, sale.NetProfit); // label included in net
    }

    [Fact]
    public async Task Reconcile_correction_preserves_the_recorded_sale_date()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Reconcile_correction_preserves_the_recorded_sale_date));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem item = SeedItem(db, org, "Brass Lamp", 40m);
        item.Status = InventoryStatus.Sold;
        DateTimeOffset recordedDate = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        db.Sales.Add(new Sale
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 70m,
            CostOfGoods = 40m,
            NetProfit = 30m,
            Status = SaleStatus.Recorded,
            SoldAt = recordedDate,
        });
        await db.SaveChangesAsync();

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        Sale sale = Assert.Single(await db.Sales.Where(s => s.InventoryItemId == item.Id).ToListAsync());
        Assert.Equal(recordedDate, sale.SoldAt);          // the operator's real date is kept, not the report range
        Assert.Equal(77.28m, sale.SoldPrice);             // money still corrected to eBay's exact figures
        Assert.Equal(5.97m, sale.ShippingCost);
    }

    [Fact]
    public async Task Ingest_rejects_negative_money()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Ingest_rejects_negative_money));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);

        IngestPlatformOrderLineInput[] lines = [new("Item", 10m)];
        ActionResult<IngestPlatformOrdersResultDto> result = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-6", lines, fee: -1m)]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.PlatformOrders.ToListAsync());
    }

    [Fact]
    public async Task Get_suggests_an_inventory_match_from_the_channel_listing_external_id()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Get_suggests_an_inventory_match_from_the_channel_listing_external_id));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        InventoryItem item = SeedItem(db, org, "Signed Poster", 15m);
        ChannelAccount account = new() { OrganizationId = org, Channel = "eBay", DisplayName = "eBay" };
        db.ChannelAccounts.Add(account);
        db.ChannelListings.Add(new ChannelListing
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            ChannelAccountId = account.Id,
            Status = ChannelListingStatus.Active,
            ExternalListingId = "EBAY-ITEM-777",
        });
        await db.SaveChangesAsync();

        IngestPlatformOrderLineInput[] lines = [new("Signed Poster", 55m, ExternalItemId: "EBAY-ITEM-777")
        ];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-7", lines)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.SingleAsync();

        ActionResult<PlatformOrderDto> detail = await controller.Get(order.Id, CancellationToken.None);
        PlatformOrderLineDto line = Assert.Single(detail.Value!.Lines);
        Assert.Equal(item.Id, line.SuggestedInventoryItemId);
        Assert.Equal("Signed Poster", line.SuggestedInventoryItemTitle);
    }

    [Fact]
    public async Task Get_suggests_a_match_by_title_when_there_is_no_link_and_flags_already_sold()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Get_suggests_a_match_by_title_when_there_is_no_link_and_flags_already_sold));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);
        // No ChannelListing link at all — matching must fall back to the title. Item is already sold.
        InventoryItem item = SeedItem(db, org, "Levi's 501 Denim Jacket", 20m);
        item.Status = InventoryStatus.Sold;
        await db.SaveChangesAsync();

        IngestPlatformOrderLineInput[] lines = [new("Vintage Levi's 501 Denim Jacket Men's Medium USA", 65m)
        ];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-11", lines)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.SingleAsync();

        ActionResult<PlatformOrderDto> detail = await controller.Get(order.Id, CancellationToken.None);
        PlatformOrderLineDto line = Assert.Single(detail.Value!.Lines);
        Assert.Equal(item.Id, line.SuggestedInventoryItemId);
        Assert.True(line.SuggestedInventoryItemAlreadySold); // tells the UI it will correct, not create
    }

    [Fact]
    public async Task Payout_variance_flags_a_mismatch_between_expected_and_scraped_payout()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Payout_variance_flags_a_mismatch_between_expected_and_scraped_payout));
        await using HaulScoutDbContext _ = db;
        PlatformOrdersController controller = NewController(db, org, user);

        // Expected payout = 100 + 10 shipping - 18 fee = 92; platform actually deposited 90 → variance -2.
        IngestPlatformOrderLineInput[] lines = [new("Item", 100m)];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-8", lines, shipping: 10m, fee: 18m, payout: 90m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.SingleAsync();

        ActionResult<PlatformOrderDto> detail = await controller.Get(order.Id, CancellationToken.None);
        Assert.Equal(92m, detail.Value!.ExpectedPayout);
        Assert.Equal(-2m, detail.Value.PayoutVariance);
    }
}
