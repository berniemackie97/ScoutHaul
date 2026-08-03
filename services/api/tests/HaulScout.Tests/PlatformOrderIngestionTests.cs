using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
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
        var currentUser = new StubCurrentUser(org, user);
        var suggestionService = new PlatformOrderSuggestionService(new EfPlatformOrderSuggestionStore(db, currentUser));
        var orderAssembler = new OrderAssembler(db, currentUser);
        var platformOrderStore = new EfPlatformOrderStore(db, orderAssembler);
        var feeEnrichment = new PlatformOrderFeeEnrichmentService(platformOrderStore, currentUser);
        var workflow = new PlatformOrderWorkflowService(platformOrderStore, currentUser, suggestionService);
        var autoMatch = new PlatformOrderAutoMatchService(suggestionService, workflow, platformOrderStore, currentUser);
        var ingestion = new PlatformOrderIngestionService(platformOrderStore, currentUser, suggestionService, workflow, autoMatch);
        return new PlatformOrdersController(
            workflow,
            new MarketplaceRefundImportService(platformOrderStore, currentUser, TimeProvider.System),
            new PlatformOrderImportWorkflowService(platformOrderStore, currentUser, ingestion, feeEnrichment),
            autoMatch);
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
        var (db, org, user) = NewContext(nameof(Ingest_creates_orders_then_reingest_updates_in_place));
        await using var _ = db;
        var controller = NewController(db, org, user);

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
    public async Task An_order_whose_lines_all_match_a_barcode_exactly_settles_itself_on_import()
    {
        var (db, org, user) = NewContext(nameof(An_order_whose_lines_all_match_a_barcode_exactly_settles_itself_on_import));
        await using var _ = db;
        var controller = NewController(db, org, user);

        InventoryItem item = SeedItem(db, org, "Leather Boots", 30m);
        item.Barcode = "SKU-BOOT-9";
        db.SaveChanges();

        // The line carries that exact code, so there is nothing for a person to decide.
        IngestPlatformOrderLineInput[] lines = [new("Leather Boots", 90m, Sku: "SKU-BOOT-9")];
        ActionResult<IngestPlatformOrdersResultDto> result = await controller.Ingest(
            new IngestPlatformOrdersRequest("eBay", [Order("ORD-AUTO", lines, fee: 9m)]), CancellationToken.None);

        Assert.Equal(1, result.Value!.AutoReconciled);

        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        Assert.Equal(PlatformOrderStatus.Reconciled, order.Status);
        Assert.Equal(item.Id, order.Lines.Single().InventoryItemId);

        // Settling means the sale exists with the platform's exact figures.
        Sale sale = await db.Sales.SingleAsync(candidate => candidate.InventoryItemId == item.Id);
        Assert.Equal(9m, sale.PlatformFees);
    }

    [Fact]
    public async Task A_marketplace_buyer_joins_the_existing_customer_with_the_same_email()
    {
        // The same person checks out on the storefront, then buys on eBay under a handle. Creating a
        // second customer would split their history — and because Email is unique per organization,
        // the insert would fail outright.
        var (db, org, user) = NewContext(nameof(A_marketplace_buyer_joins_the_existing_customer_with_the_same_email));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Leather Boots", 30m);

        Customer existing = new() { OrganizationId = org, Email = "jane@example.com", DisplayName = "Jane" };
        db.Customers.Add(existing);
        db.SaveChanges();

        IngestPlatformOrderLineInput[] lines = [new("Leather Boots", 90m)];
        IngestPlatformOrderInput order = Order("ORD-BUYER", lines) with
        {
            BuyerReference = "janedoe22",
            BuyerEmail = "JANE@example.com",
            BuyerName = "Jane D",
        };
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [order]), CancellationToken.None);

        PlatformOrder imported = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(
            imported.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(imported.Lines.First().Id, item.Id)]),
            CancellationToken.None);

        // One person, one record — with the eBay handle attached to it.
        Customer customer = Assert.Single(await db.Customers.ToListAsync());
        Assert.Equal(existing.Id, customer.Id);
        CustomerChannelIdentity identity = Assert.Single(await db.CustomerChannelIdentities.ToListAsync());
        Assert.Equal(existing.Id, identity.CustomerId);
        Assert.Equal("janedoe22", identity.ExternalUserId);
    }

    [Fact]
    public async Task An_unmatched_import_row_can_be_deleted_but_a_matched_one_cannot()
    {
        var (db, org, user) = NewContext(nameof(An_unmatched_import_row_can_be_deleted_but_a_matched_one_cannot));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Leather Boots", 30m);

        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [
            Order("ORD-JUNK", [new IngestPlatformOrderLineInput("Nothing We Own", 5m)]),
        ]), CancellationToken.None);

        PlatformOrder junk = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        Assert.Equal(PlatformOrderStatus.Imported, junk.Status);

        // A row that should not exist can be removed outright.
        ActionResult<PlatformOrderDeletedDto> deleted = await controller.Delete(junk.Id, CancellationToken.None);
        Assert.NotNull(deleted.Value);
        Assert.Empty(await db.PlatformOrders.ToListAsync());
        Assert.Empty(await db.PlatformOrderLines.ToListAsync());

        // A matched row is refused, because sales were recorded from it.
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [
            Order("ORD-REAL", [new IngestPlatformOrderLineInput("Leather Boots", 90m)]),
        ]), CancellationToken.None);
        PlatformOrder real = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(
            real.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(real.Lines.First().Id, item.Id)]),
            CancellationToken.None);

        ActionResult<PlatformOrderDeletedDto> refused = await controller.Delete(real.Id, CancellationToken.None);
        Assert.Null(refused.Value);
        Assert.Single(await db.PlatformOrders.ToListAsync());
    }

    [Fact]
    public async Task The_backlog_preview_counts_what_can_settle_without_writing_anything()
    {
        var (db, org, user) = NewContext(nameof(The_backlog_preview_counts_what_can_settle_without_writing_anything));
        await using var _ = db;
        var controller = NewController(db, org, user);

        // One order identifiable by barcode, one only by title.
        InventoryItem certain = SeedItem(db, org, "Leather Boots", 30m);
        certain.Barcode = "SKU-BOOT-9";
        SeedItem(db, org, "Vintage Denim Jacket", 20m);
        db.SaveChanges();

        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [
            Order("ORD-CERTAIN", [new IngestPlatformOrderLineInput("Leather Boots", 90m, Sku: "SKU-BOOT-9")]),
            Order("ORD-FUZZY", [new IngestPlatformOrderLineInput("Vintage Denim Jacket", 60m)]),
        ]), CancellationToken.None);

        // The certain one settles during import, so the backlog holds only the fuzzy one — and the
        // preview must report that nothing there can settle itself.
        ActionResult<AutoMatchPreviewDto> preview = await controller.PreviewAutoMatch(CancellationToken.None);
        Assert.Equal(1, preview.Value!.PendingOrders);
        Assert.Equal(0, preview.Value.CanSettle);

        // Previewing writes nothing: the fuzzy order is untouched and has no sale.
        PlatformOrder fuzzy = await db.PlatformOrders.SingleAsync(o => o.ExternalOrderId == "ORD-FUZZY");
        Assert.Equal(PlatformOrderStatus.Imported, fuzzy.Status);
        Assert.Single(await db.Sales.ToListAsync());
    }

    [Fact]
    public async Task An_order_matched_only_by_title_is_left_for_a_person()
    {
        var (db, org, user) = NewContext(nameof(An_order_matched_only_by_title_is_left_for_a_person));
        await using var _ = db;
        var controller = NewController(db, org, user);

        // Title resembles the item, but nothing identifies it. A wrong sale is worse than a queue entry.
        SeedItem(db, org, "Leather Boots Size 9", 30m);
        IngestPlatformOrderLineInput[] lines = [new("Leather Boots Size 9", 90m)];

        ActionResult<IngestPlatformOrdersResultDto> result = await controller.Ingest(
            new IngestPlatformOrdersRequest("eBay", [Order("ORD-FUZZY", lines, fee: 9m)]), CancellationToken.None);

        Assert.Equal(0, result.Value!.AutoReconciled);

        PlatformOrder order = await db.PlatformOrders.SingleAsync();
        Assert.Equal(PlatformOrderStatus.Imported, order.Status);
        Assert.Empty(await db.Sales.ToListAsync());
    }

    [Fact]
    public async Task Reingesting_a_reconciled_order_corrects_its_sale_without_re_queuing_it()
    {
        var (db, org, user) = NewContext(nameof(Reingesting_a_reconciled_order_corrects_its_sale_without_re_queuing_it));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Leather Boots", 30m);

        IngestPlatformOrderLineInput[] lines = [new("Leather Boots", 90m, ExternalItemId: "EBAY-BOOTS-9")];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-9", lines, fee: 12m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id, new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        ActionResult<IngestPlatformOrdersResultDto> reingest = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-9", lines, fee: 15m)]), CancellationToken.None);
        Assert.Equal(0, reingest.Value!.Skipped);
        Assert.Equal(1, reingest.Value.Updated);

        PlatformOrder refreshed = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        Assert.Equal(15m, refreshed.FeeTotal);
        Assert.Equal(item.Id, refreshed.Lines.Single().InventoryItemId);

        // The order stays settled: its line is still mapped, so re-importing must not send work the
        // operator already did back into the queue.
        Assert.Equal(PlatformOrderStatus.Reconciled, refreshed.Status);

        // ...and the corrected fee still reaches the sale, without anyone reconciling it again.
        Sale corrected = await db.Sales.SingleAsync(sale => sale.InventoryItemId == item.Id);
        Assert.Equal(15m, corrected.PlatformFees);
        Assert.Single(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Reconcile_single_line_pushes_exact_platform_figures_into_the_sale()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_single_line_pushes_exact_platform_figures_into_the_sale));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

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
        var (db, org, user) = NewContext(nameof(Reconcile_multi_line_allocates_shared_costs_and_sums_back_to_the_order));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var a = SeedItem(db, org, "Item A", 10m);
        var b = SeedItem(db, org, "Item B", 20m);

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
        var (db, org, user) = NewContext(nameof(Reconcile_requires_every_line_to_be_mapped));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var a = SeedItem(db, org, "Item A", 10m);

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
        var (db, org, user) = NewContext(nameof(Reconcile_corrects_a_hand_recorded_sale_in_place_with_exact_fees));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);
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
        var (db, org, user) = NewContext(nameof(Reconcile_mixes_a_new_sale_and_a_correction_in_one_order));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var fresh = SeedItem(db, org, "Fresh Item", 10m);
        var recorded = SeedItem(db, org, "Recorded Item", 15m);
        recorded.Status = InventoryStatus.Sold;
        db.Sales.Add(new Sale
        {
            OrganizationId = org,
            InventoryItemId = recorded.Id,
            Platform = "eBay",
            SoldPrice = 50m,
            CostOfGoods = 15m,
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

    private const string EbayAllOrdersCsv = """
Order Number,Buyer Username,Buyer Name,Buyer Email,Buyer Note,Ship To Name,Ship To Phone,Ship To Address 1,Ship To Address 2,Ship To City,Ship To State,Ship To Zip,Ship To Country,Sale Date,Paid On Date,Shipped On Date,Shipping Service,Tracking Number,Item Number,Item Title,Custom Label,Quantity,Sold For,Shipping And Handling,Seller Collected Tax,eBay Collected Tax,Total Price
27-12513-18593,brassbuyer,Brass Buyer,buyer@example.com,,Brass Buyer,,1 Main St,,Scranton,PA,18503,United States,Jun-30-26,Jun-30-26,Jul-1-26,USPS Ground Advantage,9400111100000000000000,377251318593,Brass Lamp,SKU-LAMP,1,$77.28,$3.69,$0.00,$7.15,$88.12
""";

    private const string EbayListingSalesCsv = """
"Report for Apr 9, 2026 to Jul 7, 2026"
Listing title,eBay item ID,Store category L1,Store category L2,Quantity sold,Total sales (Includes taxes),Item sales,Taxes and government fees paid by buyer to you,Taxes and government fees paid by buyer to eBay,Shipping and handling paid by buyer to you,Total selling costs,Insertion fees,Optional listing upgrade fees,Final value fees,Promoted Listings - General fees,Promoted Listings - Priority fees,Ads Express fees,Promoted Offsite - Fees,International fees,Other eBay fees,Deposit processing fees,Fee credits,Shipping labels cost (Amount you paid to buy shipping labels on eBay),Net sales (Net of taxes and selling costs),Average Selling price,Quantity sold via promoted listing,Quantity sold via Best Offers,Quantity sold via Seller Initiated Offers,
Brass Lamp,377251318593,-,-,1,$88.12,$77.28,$0.00,$7.15,$3.69,$18.05,$0.00,$0.00,$19.29,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$7.21,$5.97,$62.92,$88.12,0,1,0
""";

    private const string EbayRepeatListingAllOrdersCsv = """
Order Number,Buyer Username,Buyer Name,Buyer Email,Buyer Note,Ship To Name,Ship To Phone,Ship To Address 1,Ship To Address 2,Ship To City,Ship To State,Ship To Zip,Ship To Country,Sale Date,Paid On Date,Shipped On Date,Shipping Service,Tracking Number,Item Number,Item Title,Custom Label,Quantity,Sold For,Shipping And Handling,Seller Collected Tax,eBay Collected Tax,Total Price
ORD-A,buyer-a,Buyer A,,,,,,,,,,United States,Jun-30-26,Jun-30-26,,,TRACK-A,REPEAT-ITEM,Repeat Listing Item,,1,$60.00,$6.00,$0.00,$0.00,$66.00
ORD-B,buyer-b,Buyer B,,,,,,,,,,United States,Jul-1-26,Jul-1-26,,,TRACK-B,REPEAT-ITEM,Repeat Listing Item,,1,$40.00,$4.00,$0.00,$0.00,$44.00
""";

    private const string EbayRepeatListingSalesCsv = """
"Report for Jun 1, 2026 to Jul 7, 2026"
Listing title,eBay item ID,Store category L1,Store category L2,Quantity sold,Total sales (Includes taxes),Item sales,Taxes and government fees paid by buyer to you,Taxes and government fees paid by buyer to eBay,Shipping and handling paid by buyer to you,Total selling costs,Insertion fees,Optional listing upgrade fees,Final value fees,Promoted Listings - General fees,Promoted Listings - Priority fees,Ads Express fees,Promoted Offsite - Fees,International fees,Other eBay fees,Deposit processing fees,Fee credits,Shipping labels cost (Amount you paid to buy shipping labels on eBay),Net sales (Net of taxes and selling costs),Average Selling price,Quantity sold via promoted listing,Quantity sold via Best Offers,Quantity sold via Seller Initiated Offers,
Repeat Listing Item,REPEAT-ITEM,-,-,2,$110.00,$100.00,$0.00,$0.00,$10.00,$20.00,$0.00,$0.00,$14.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$6.00,$84.00,$55.00,0,0,0
""";

    [Fact]
    public async Task Import_ebay_csv_ingests_then_reimports_idempotently()
    {
        var (db, org, user) = NewContext(nameof(Import_ebay_csv_ingests_then_reimports_idempotently));
        await using var _ = db;
        var controller = NewController(db, org, user);

        ActionResult<IngestPlatformOrdersResultDto> first = await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        Assert.Equal(1, first.Value!.Created);

        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        Assert.Equal("27-12513-18593", order.ExternalOrderId);
        Assert.Equal("brassbuyer", order.BuyerReference);
        Assert.Equal("9400111100000000000000", order.TrackingNumber);
        Assert.Equal(0m, order.FeeTotal);                // the all-orders report has order truth, not fee truth

        ActionResult<IngestPlatformOrdersResultDto> enrichment = await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        Assert.Equal(1, enrichment.Value!.Updated);
        order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        Assert.Equal(12.08m, order.FeeTotal);            // exact eBay cut (18.05 − 5.97 label)
        Assert.Equal(5.97m, order.SellerShippingCost);
        Assert.Equal(62.92m, order.PayoutAmount);

        ActionResult<IngestPlatformOrdersResultDto> second = await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        Assert.Equal(0, second.Value!.Created);
        Assert.Equal(1, second.Value.Updated);
        Assert.Single(await db.PlatformOrders.ToListAsync());
    }

    [Fact]
    public async Task Listing_sales_enrichment_allocates_repeat_listing_totals_without_double_counting()
    {
        var (db, org, user) = NewContext(nameof(Listing_sales_enrichment_allocates_repeat_listing_totals_without_double_counting));
        await using var _ = db;
        var controller = NewController(db, org, user);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayRepeatListingAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayRepeatListingSalesCsv), CancellationToken.None);

        List<PlatformOrderLine> lines = await db.PlatformOrderLines
            .Include(line => line.PlatformOrder)
            .OrderBy(line => line.PlatformOrder!.ExternalOrderId)
            .ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(14.00m, lines.Sum(line => line.PlatformFees));
        Assert.Equal(6.00m, lines.Sum(line => line.SellerShippingCost));
        Assert.Equal(84.00m, lines.Sum(line => line.NetSales));
        Assert.Equal(8.40m, lines[0].PlatformFees);
        Assert.Equal(5.60m, lines[1].PlatformFees);
        Assert.Equal(3.60m, lines[0].SellerShippingCost);
        Assert.Equal(2.40m, lines[1].SellerShippingCost);
    }

    [Fact]
    public async Task Reconcile_new_sale_from_csv_carries_the_shipping_label_cost()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_new_sale_from_csv_carries_the_shipping_label_cost));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
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
    public async Task Listing_sales_import_corrects_an_already_reconciled_sale()
    {
        var (db, org, user) = NewContext(nameof(Listing_sales_import_corrects_an_already_reconciled_sale));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        Sale before = await db.Sales.SingleAsync(sale => sale.InventoryItemId == item.Id);
        Assert.Equal(0m, before.PlatformFees);
        Assert.Equal(0m, before.ShippingCost);

        ActionResult<IngestPlatformOrdersResultDto> enrichment = await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        Assert.Equal(2, enrichment.Value!.Updated); // one imported line refreshed + one posted sale corrected

        Sale sale = await db.Sales.SingleAsync(sale => sale.InventoryItemId == item.Id);
        Assert.Equal(12.08m, sale.PlatformFees);
        Assert.Equal(5.97m, sale.ShippingCost);
        Assert.Equal(3.69m, sale.ShippingCharged);
        Assert.Equal(7.15m, sale.SalesTaxCollected);
        Assert.Equal(77.28m + 3.69m - 40m - 12.08m - 5.97m, sale.NetProfit);
        Assert.Single(await db.SaleCorrections.Where(correction => correction.SaleId == sale.Id).ToListAsync());
    }

    private const string EbayRefundListingSalesCsv = """
"Report for Jan 1, 2026 to Jul 10, 2026"
Listing title,eBay item ID,Quantity sold,Total sales (Includes taxes),Item sales,Taxes and government fees paid by buyer to you,Taxes and government fees paid by buyer to eBay,Shipping and handling paid by buyer to you,Total selling costs,Insertion fees,Optional listing upgrade fees,Final value fees,Promoted Listings - General fees,Promoted Listings - Priority fees,Ads Express fees,Promoted Offsite - Fees,International fees,Other eBay fees,Deposit processing fees,Fee credits,Shipping labels cost (Amount you paid to buy shipping labels on eBay),Net sales (Net of taxes and selling costs),Average Selling price,Quantity sold via promoted listing,Quantity sold via Best Offers,Quantity sold via Seller Initiated Offers,
Brass Lamp,377251318593,1,$0.00,$0.00,$0.00,$0.00,$0.00,$12.34,$0.00,$0.00,$16.90,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$16.50,$11.94,($12.34),$0.00,0,1,0
""";

    [Fact]
    public async Task Refund_import_posts_ebay_fee_credit_row_to_exact_reconciled_sale()
    {
        var (db, org, user) = NewContext(nameof(Refund_import_posts_ebay_fee_credit_row_to_exact_reconciled_sale));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        ActionResult<ImportMarketplaceRefundsResultDto> result = await controller.ImportMarketplaceRefundsCsv(
            new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        Assert.Equal(1, result.Value!.Imported);
        Refund refund = Assert.Single(await db.Refunds.ToListAsync());
        Assert.Equal(RefundType.Full, refund.Type);
        Assert.Equal(77.28m, refund.ItemAmount);
        Assert.Equal(3.69m, refund.ShippingRefunded);
        Assert.Equal(7.15m, refund.TaxRefunded);
        Assert.Equal(12.08m, refund.FeeCredit); // capped to the sale's actual fee exposure

        Sale sale = await db.Sales.SingleAsync();
        Assert.Equal(SaleStatus.Refunded, sale.Status);
        Assert.Equal(-45.97m, sale.NetProfit);

        // Re-import is an idempotent upsert: it refreshes the one refund from the same facts (so it counts as
        // processed), and the state does not drift — still one refund, same amounts, same net.
        ActionResult<ImportMarketplaceRefundsResultDto> second = await controller.ImportMarketplaceRefundsCsv(
            new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);
        Assert.Equal(1, second.Value!.Imported);
        Refund afterReimport = Assert.Single(await db.Refunds.ToListAsync());
        Assert.Equal(12.08m, afterReimport.FeeCredit);
        Assert.Equal(-45.97m, (await db.Sales.SingleAsync()).NetProfit);
    }

    /// <summary>
    /// Legacy adoption: a refund imported before ExternalRefundKey existed (its dedupe id lived in Notes, key null)
    /// must be UPGRADED in place on the next re-import, never duplicated — otherwise the projection would double
    /// every already-imported prod refund.
    /// </summary>
    [Fact]
    public async Task Reimporting_adopts_a_legacy_keyless_refund_instead_of_duplicating_it()
    {
        var (db, org, user) = NewContext(nameof(Reimporting_adopts_a_legacy_keyless_refund_instead_of_duplicating_it));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);

        // Simulate a pre-migration prod refund: imported, but with the OLD shape — no key, fingerprint in Notes.
        Sale seeded = await db.Sales.SingleAsync();
        db.Refunds.Add(new Refund
        {
            OrganizationId = org,
            SaleId = seeded.Id,
            Type = RefundType.Full,
            ItemAmount = 77.28m,
            ShippingRefunded = 3.69m,
            TaxRefunded = 7.15m,
            FeeCredit = 0m,                                  // was capped before fees; should heal on re-import
            Reason = "Imported from eBay Listing Sales",
            Notes = "legacy [import:deadbeef]",
            ExternalRefundKey = null,
            RecordedByUserId = user,
        });
        await db.SaveChangesAsync();

        await controller.ImportMarketplaceRefundsCsv(new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        Refund refund = Assert.Single(await db.Refunds.ToListAsync());   // adopted, not duplicated
        Assert.NotNull(refund.ExternalRefundKey);                        // stamped with the real key
        Assert.Equal(12.08m, refund.FeeCredit);                          // and healed in the same pass
    }

    /// <summary>
    /// The convergence property, on the exact bug from the screenshots: the refund report imported BEFORE the fee
    /// report, so the fee credit capped against zero fees and net was wrong. The credit figure lives in the refund
    /// report, so re-importing it after the fees land heals the credit to the truth. Same single refund throughout;
    /// end state is eBay's figure to the cent, reached by the operator's natural habit of re-importing.
    /// </summary>
    [Fact]
    public async Task Reimporting_a_refund_after_fees_land_heals_the_capped_credit()
    {
        var (db, org, user) = NewContext(nameof(Reimporting_a_refund_after_fees_land_heals_the_capped_credit));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        // All Orders + reconcile: the sale exists but its fees are still zero (the fee report has not arrived).
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        // Refund imports first — fee exposure is zero, so the credit caps at zero.
        await controller.ImportMarketplaceRefundsCsv(new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);
        Assert.Equal(0m, (await db.Refunds.SingleAsync()).FeeCredit);

        // The fee report lands; the refunded sale's fees are corrected, but the refund's stored credit is still zero.
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        Assert.Equal(0m, (await db.Refunds.SingleAsync()).FeeCredit);   // not yet healed

        // Re-import the refund report — the upsert refreshes the one refund against the now-known fees.
        await controller.ImportMarketplaceRefundsCsv(new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        Refund refund = Assert.Single(await db.Refunds.ToListAsync());   // never duplicated
        Assert.Equal(12.08m, refund.FeeCredit);                          // healed to the sale's true exposure
        Assert.Equal(-45.97m, (await db.Sales.SingleAsync()).NetProfit); // eBay parity
    }

    /// <summary>
    /// A return opened AFTER the refund already imported used to stay stuck forever: the old dedupe skipped the
    /// re-import, so the resolution never ran. The upsert re-runs the resolution against the open return every
    /// import, so re-importing the refund report closes it.
    /// </summary>
    [Fact]
    public async Task Reimporting_a_refund_resolves_a_return_opened_after_the_first_import()
    {
        var (db, org, user) = NewContext(nameof(Reimporting_a_refund_resolves_a_return_opened_after_the_first_import));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        await controller.ImportMarketplaceRefundsCsv(new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        // The operator opens and receives the return AFTER the refund already imported.
        Sale saleMid = await db.Sales.Include(s => s.Returns).SingleAsync();
        Return rma = Return.Open(org, saleMid, "damage", null, user, DateTimeOffset.UtcNow.AddDays(-1));
        rma.Receive(DateTimeOffset.UtcNow);
        db.Returns.Add(rma);
        await db.SaveChangesAsync();

        // Re-import the refund report — the stuck return resolves.
        await controller.ImportMarketplaceRefundsCsv(new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        Return resolved = await db.Returns.SingleAsync();
        Assert.Equal(ReturnStatus.Refunded, resolved.Status);
        Assert.True(resolved.Restocked);
        Assert.Single(await db.Refunds.ToListAsync());   // still one refund — the resolution reused it
        Assert.Equal(SaleStatus.Returned, (await db.Sales.SingleAsync()).Status);
        Assert.Equal(InventoryStatus.Returned, (await db.InventoryItems.SingleAsync(i => i.Id == item.Id)).Status);
    }

    /// <summary>
    /// The screenshot bug: eBay refunded the buyer, the refund imported, and the return sat open at Received
    /// forever — with the sale badge saying Refunded while the item and the return disagreed. An imported refund
    /// IS the open return's resolution: one event, every status derived from it by the one rule.
    /// </summary>
    [Fact]
    public async Task Refund_import_resolves_a_received_return_and_restocks_the_item()
    {
        var (db, org, user) = NewContext(nameof(Refund_import_resolves_a_received_return_and_restocks_the_item));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        // The operator ran the return workflow: requested, label sent, item received back — then eBay's refund
        // landed in the next Listing Sales export instead of being issued from our workflow.
        Sale saleBefore = await db.Sales.SingleAsync();
        Return rma = Return.Open(org, saleBefore, "damage", null, user, DateTimeOffset.UtcNow.AddDays(-2));
        rma.IssueLabel(11.94m, null, DateTimeOffset.UtcNow.AddDays(-2));
        rma.Receive(DateTimeOffset.UtcNow.AddDays(-1));
        db.Returns.Add(rma);
        await db.SaveChangesAsync();

        await controller.ImportMarketplaceRefundsCsv(
            new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        // One truth, everywhere it is read.
        Return resolved = await db.Returns.SingleAsync();
        Assert.Equal(ReturnStatus.Refunded, resolved.Status);
        Assert.True(resolved.Restocked);
        Assert.NotNull(resolved.RefundId);
        Refund refund = Assert.Single(await db.Refunds.ToListAsync());
        Assert.True(refund.ItemReturned);
        Sale sale = await db.Sales.SingleAsync();
        Assert.Equal(SaleStatus.Returned, sale.Status);
        Assert.Equal(InventoryStatus.Returned, (await db.InventoryItems.SingleAsync(i => i.Id == item.Id)).Status);
        // Item back on the shelf: COGS released; the original outbound label stays spent.
        Assert.Equal(-5.97m, sale.NetProfit);
    }

    [Fact]
    public async Task Reimporting_a_returned_order_corrects_the_returned_sale_instead_of_creating_a_duplicate()
    {
        var (db, org, user) = NewContext(nameof(Reimporting_a_returned_order_corrects_the_returned_sale_instead_of_creating_a_duplicate));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        Sale saleBefore = await db.Sales.SingleAsync();
        Return rma = Return.Open(org, saleBefore, "damage", null, user, DateTimeOffset.UtcNow.AddDays(-2));
        rma.IssueLabel(11.94m, null, DateTimeOffset.UtcNow.AddDays(-2));
        rma.Receive(DateTimeOffset.UtcNow.AddDays(-1));
        db.Returns.Add(rma);
        await db.SaveChangesAsync();

        await controller.ImportMarketplaceRefundsCsv(
            new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        Sale returnedSale = await db.Sales.Include(sale => sale.Refunds).Include(sale => sale.Returns).SingleAsync();
        Assert.Equal(SaleStatus.Returned, returnedSale.Status);

        // Simulates the bad production state caused by the previous reconcile rule: the same eBay order also has a
        // fresh recorded sale for the same inventory item. Shipment evidence is moved to the surviving returned sale.
        Order salesOrder = await db.Orders.SingleAsync();
        Sale duplicate = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 77.28m,
            ShippingCharged = 3.69m,
            CostOfGoods = 40m,
            SalesTaxCollected = 7.15m,
            TaxRemittedByPlatform = true,
            SoldAt = OrderedAt,
            Status = SaleStatus.Recorded,
            OrderId = salesOrder.Id,
        };
        duplicate.RecomputeNet();
        db.Sales.Add(duplicate);
        db.Shipments.Add(new Shipment
        {
            OrganizationId = org,
            SaleId = duplicate.Id,
            Carrier = "USPS Ground Advantage",
            TrackingNumber = "9400111100000000000000",
            ShippedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Sales.CountAsync());

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        PlatformOrder refreshed = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(refreshed.Id, new ReconcilePlatformOrderRequest([]), CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedSaleCount);
        Assert.Equal(1, result.Value.CorrectedSaleCount);
        Sale surviving = Assert.Single(await db.Sales.Include(sale => sale.Refunds).Include(sale => sale.Returns).ToListAsync());
        Assert.Equal(returnedSale.Id, surviving.Id);
        Assert.Equal(SaleStatus.Returned, surviving.Status);
        Assert.Single(surviving.Refunds);
        Assert.Single(surviving.Returns);

        Shipment shipment = await db.Shipments.SingleAsync();
        Assert.Equal(surviving.Id, shipment.SaleId);

        Order repairedOrder = await db.Orders.Include(item => item.Lines).SingleAsync();
        Assert.Equal(77.28m, repairedOrder.Subtotal);
        Assert.Equal(3.69m, repairedOrder.ShippingTotal);
        Assert.Equal(7.15m, repairedOrder.TaxTotal);
        Assert.Equal(88.12m, repairedOrder.GrandTotal);
        Assert.Equal(OrderStatus.Refunded, repairedOrder.Status);
        Assert.Equal(PaymentStatus.Refunded, repairedOrder.PaymentStatus);
        Assert.Single(repairedOrder.Lines);
    }

    [Fact]
    public async Task A_returned_item_can_be_reconciled_to_a_later_different_order_as_a_true_resale()
    {
        var (db, org, user) = NewContext(nameof(A_returned_item_can_be_reconciled_to_a_later_different_order_as_a_true_resale));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        PlatformOrder firstOrder = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(firstOrder.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(firstOrder.Lines.First().Id, item.Id)]), CancellationToken.None);

        Sale firstSale = await db.Sales.SingleAsync();
        Return rma = Return.Open(org, firstSale, "damage", null, user, DateTimeOffset.UtcNow.AddDays(-2));
        rma.IssueLabel(11.94m, null, DateTimeOffset.UtcNow.AddDays(-2));
        rma.Receive(DateTimeOffset.UtcNow.AddDays(-1));
        db.Returns.Add(rma);
        await db.SaveChangesAsync();
        await controller.ImportMarketplaceRefundsCsv(
            new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        IngestPlatformOrderLineInput[] resaleLines = [new("Brass Lamp", 110m, ExternalItemId: "377251318593")];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-RESOLD", resaleLines, shipping: 5.97m, tax: 8.53m, fee: 14m)]), CancellationToken.None);
        PlatformOrder resaleOrder = await db.PlatformOrders.Include(o => o.Lines).SingleAsync(item => item.ExternalOrderId == "ORD-RESOLD");
        ActionResult<PlatformOrderReconcileResultDto> result = await controller.Reconcile(resaleOrder.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(resaleOrder.Lines.First().Id, item.Id)]), CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedSaleCount);
        Assert.Equal(0, result.Value.CorrectedSaleCount);
        List<Sale> sales = await db.Sales.OrderBy(sale => sale.SoldAt).ToListAsync();
        Assert.Equal(2, sales.Count);
        Assert.Equal(SaleStatus.Returned, sales[0].Status);
        Assert.Equal(SaleStatus.Recorded, sales[1].Status);
        Assert.NotEqual(sales[0].OrderId, sales[1].OrderId);
        Assert.Equal(InventoryStatus.Sold, (await db.InventoryItems.SingleAsync(i => i.Id == item.Id)).Status);
    }

    /// <summary>An early marketplace refund — item still with the buyer — closes the return WITHOUT restocking:
    /// claiming the item is back would be a lie the books repeat forever.</summary>
    [Fact]
    public async Task Refund_import_resolves_an_unreceived_return_without_restocking()
    {
        var (db, org, user) = NewContext(nameof(Refund_import_resolves_an_unreceived_return_without_restocking));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.Include(o => o.Lines).SingleAsync();
        await controller.Reconcile(order.Id,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(order.Lines.First().Id, item.Id)]), CancellationToken.None);

        Sale saleBefore = await db.Sales.SingleAsync();
        Return rma = Return.Open(org, saleBefore, "damage", null, user, DateTimeOffset.UtcNow.AddDays(-1));
        rma.IssueLabel(11.94m, null, DateTimeOffset.UtcNow.AddDays(-1));   // label out, item still with the buyer
        db.Returns.Add(rma);
        await db.SaveChangesAsync();

        await controller.ImportMarketplaceRefundsCsv(
            new ImportMarketplaceRefundsCsvRequest("eBay", EbayRefundListingSalesCsv), CancellationToken.None);

        Return resolved = await db.Returns.SingleAsync();
        Assert.Equal(ReturnStatus.Refunded, resolved.Status);
        Assert.False(resolved.Restocked);
        Assert.False((await db.Refunds.SingleAsync()).ItemReturned);
        Sale sale = await db.Sales.SingleAsync();
        Assert.Equal(SaleStatus.Refunded, sale.Status);   // refunded, not returned — the item is still out
        Assert.NotEqual(InventoryStatus.Returned, (await db.InventoryItems.SingleAsync(i => i.Id == item.Id)).Status);
        Assert.Equal(-45.97m, sale.NetProfit);   // COGS stays consumed until the item actually comes back
    }

    private const string DepopRefundCsv = """"
"Date of sale","Time of sale","Buyer","Brand","SKU","Description","Size","Item price","Buyer shipping cost","Total","US Sales tax","Refunded to buyer amount","Fees refunded to seller"
"07/03/2026","1:46 PM","strickss","Vans","N/A","Vans Shorts Mens 34 Blue Chino Denim Casual Skate 5 pockets
#skater #summer","34""","$5.00","$0.00","$5.42","$0.42","$6.37","$2.00"
"""";

    [Fact]
    public async Task Refund_import_posts_depop_refund_when_one_sale_matches()
    {
        var (db, org, user) = NewContext(nameof(Refund_import_posts_depop_refund_when_one_sale_matches));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Vans Shorts Mens 34 Blue Chino Denim Casual Skate 5 pockets", 1m);
        db.Sales.Add(new Sale
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            InventoryItem = item,
            Platform = "Depop",
            SoldPrice = 5m,
            SalesTaxCollected = 0.42m,
            PlatformFees = 2m,
            CostOfGoods = 1m,
            SoldAt = new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero),
            Status = SaleStatus.Delivered,
        });
        await db.SaveChangesAsync();

        ActionResult<ImportMarketplaceRefundsResultDto> result = await controller.ImportMarketplaceRefundsCsv(
            new ImportMarketplaceRefundsCsvRequest("Depop", DepopRefundCsv), CancellationToken.None);

        Assert.Equal(1, result.Value!.Imported);
        Refund refund = Assert.Single(await db.Refunds.ToListAsync());
        Assert.Equal(RefundType.Full, refund.Type);
        Assert.Equal(5m, refund.ItemAmount);
        Assert.Equal(0.42m, refund.TaxRefunded);
        Assert.Equal(2m, refund.FeeCredit);

        Sale sale = await db.Sales.SingleAsync();
        Assert.Equal(SaleStatus.Refunded, sale.Status);
        Assert.Equal(-1m, sale.NetProfit);
    }

    [Fact]
    public async Task Reconcile_correction_preserves_the_recorded_sale_date()
    {
        var (db, org, user) = NewContext(nameof(Reconcile_correction_preserves_the_recorded_sale_date));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Brass Lamp", 40m);
        item.Status = InventoryStatus.Sold;
        DateTimeOffset recordedDate = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        db.Sales.Add(new Sale
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 70m,
            CostOfGoods = 40m,
            Status = SaleStatus.Recorded,
            SoldAt = recordedDate,
        });
        await db.SaveChangesAsync();

        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayAllOrdersCsv), CancellationToken.None);
        await controller.ImportEbayCsv(new ImportPlatformOrdersCsvRequest("eBay", EbayListingSalesCsv), CancellationToken.None);
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
        var (db, org, user) = NewContext(nameof(Ingest_rejects_negative_money));
        await using var _ = db;
        var controller = NewController(db, org, user);

        IngestPlatformOrderLineInput[] lines = [new("Item", 10m)];
        ActionResult<IngestPlatformOrdersResultDto> result = await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-6", lines, fee: -1m)]), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.PlatformOrders.ToListAsync());
    }

    [Fact]
    public async Task A_line_matching_a_channel_listing_external_id_is_settled_not_merely_suggested()
    {
        var (db, org, user) = NewContext(nameof(A_line_matching_a_channel_listing_external_id_is_settled_not_merely_suggested));
        await using var _ = db;
        var controller = NewController(db, org, user);
        var item = SeedItem(db, org, "Signed Poster", 15m);
        var account = new ChannelAccount { OrganizationId = org, Channel = "eBay", DisplayName = "eBay" };
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

        // The platform's own item number resolves to this item exactly, so there is nothing to ask:
        // the import settles it rather than parking a suggestion in the queue.
        Assert.Equal(PlatformOrderStatus.Reconciled, order.Status);

        ActionResult<PlatformOrderDto> detail = await controller.Get(order.Id, CancellationToken.None);
        PlatformOrderLineDto line = Assert.Single(detail.Value!.Lines);
        Assert.Equal(item.Id, line.InventoryItemId);
    }

    [Fact]
    public async Task Get_suggests_a_match_by_title_when_there_is_no_link_and_flags_already_sold()
    {
        var (db, org, user) = NewContext(nameof(Get_suggests_a_match_by_title_when_there_is_no_link_and_flags_already_sold));
        await using var _ = db;
        var controller = NewController(db, org, user);
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
        var (db, org, user) = NewContext(nameof(Payout_variance_flags_a_mismatch_between_expected_and_scraped_payout));
        await using var _ = db;
        var controller = NewController(db, org, user);

        // Expected payout = 100 + 10 shipping - 18 fee = 92; platform actually deposited 90 → variance -2.
        IngestPlatformOrderLineInput[] lines = [new("Item", 100m)];
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [Order("ORD-8", lines, shipping: 10m, fee: 18m, payout: 90m)]), CancellationToken.None);
        PlatformOrder order = await db.PlatformOrders.SingleAsync();

        ActionResult<PlatformOrderDto> detail = await controller.Get(order.Id, CancellationToken.None);
        Assert.Equal(92m, detail.Value!.ExpectedPayout);
        Assert.Equal(-2m, detail.Value.PayoutVariance);
    }
}
