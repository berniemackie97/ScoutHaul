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

/// <summary>
/// Orders and Customers must mean the same thing on every channel — the storefront is simply channel #1.
/// These pin the behaviour that replaced the storefront-only Order/Customer and the SaleGroupId order-header hack.
/// </summary>
public class OrderChannelUnificationTests
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

    private static PlatformOrdersController NewPlatformOrders(HaulScoutDbContext db, Guid org, Guid user)
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

    private static SalesController NewSales(HaulScoutDbContext db, Guid org, Guid user)
    {
        var currentUser = new StubCurrentUser(org, user);
        return SalesControllerFactory.Create(db, currentUser);
    }

    private static OrdersController NewOrders(HaulScoutDbContext db, Guid org, Guid user)
    {
        var currentUser = new StubCurrentUser(org, user);
        return new OrdersController(new OrdersWorkflowService(db, currentUser, new OrderAssembler(db, currentUser)));
    }

    private static async Task<InventoryItem> SeedItem(HaulScoutDbContext db, Guid org, string title)
    {
        var item = new InventoryItem { OrganizationId = org, Title = title, PurchasePrice = 10m, Status = InventoryStatus.Listed };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task<(Guid ItemId, Guid PlatformOrderId, Guid LineId)> SeedEbayOrder(
        HaulScoutDbContext db, Guid org, Guid user, string externalOrderId, string buyer)
    {
        var item = new InventoryItem { OrganizationId = org, Title = "Brass Lamp", PurchasePrice = 40m, Status = InventoryStatus.Listed };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        var controller = NewPlatformOrders(db, org, user);
        await controller.Ingest(new IngestPlatformOrdersRequest("eBay", [
            new IngestPlatformOrderInput(
                ExternalOrderId: externalOrderId,
                OrderedAt: DateTimeOffset.UtcNow.AddDays(-2),
                ItemsSubtotal: 120m,
                ShippingCharged: 10m,
                TaxCollected: 9m,
                FeeTotal: 18m,
                Lines: [new IngestPlatformOrderLineInput("Brass Lamp", 120m)],
                BuyerReference: buyer,
                BuyerName: "Some Buyer")
        ]), CancellationToken.None);

        var platformOrder = await db.PlatformOrders.Include(o => o.Lines).SingleAsync(o => o.ExternalOrderId == externalOrderId);
        return (item.Id, platformOrder.Id, platformOrder.Lines.First().Id);
    }

    [Fact]
    public async Task Reconciling_a_marketplace_order_creates_a_real_Order_on_that_channel()
    {
        var (db, org, user) = NewContext(nameof(Reconciling_a_marketplace_order_creates_a_real_Order_on_that_channel));
        await using var _ = db;
        var (itemId, platformOrderId, lineId) = await SeedEbayOrder(db, org, user, "EB-ORDER-1", "buyer_bob");

        await NewPlatformOrders(db, org, user).Reconcile(platformOrderId,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(lineId, itemId)]), CancellationToken.None);

        // An eBay order is an Order — same entity the storefront uses.
        var order = Assert.Single(await db.Orders.ToListAsync());
        Assert.Equal("eBay", order.Channel);
        Assert.Equal("EB-ORDER-1", order.ExternalOrderId);
        Assert.Equal("buyer_bob", order.BuyerReference);
        Assert.Null(order.Email);                       // marketplace buyers have no email
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(139m, order.GrandTotal);           // 120 item + 10 shipping + 9 tax

        // The sale is a line of that order — not a naked row carrying a group id.
        var sale = Assert.Single(await db.Sales.ToListAsync());
        Assert.Equal(order.Id, sale.OrderId);
    }

    [Fact]
    public async Task Order_detail_exposes_reconciled_sale_financials_on_each_line()
    {
        var (db, org, user) = NewContext(nameof(Order_detail_exposes_reconciled_sale_financials_on_each_line));
        await using var _ = db;
        var (itemId, platformOrderId, lineId) = await SeedEbayOrder(db, org, user, "EB-ORDER-FIN", "buyer_bob");

        await NewPlatformOrders(db, org, user).Reconcile(platformOrderId,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(lineId, itemId)]), CancellationToken.None);

        Order order = await db.Orders.SingleAsync();
        ActionResult<OrderDetailDto> result = await NewOrders(db, org, user).Get(order.Id, CancellationToken.None);

        OrderLineDto line = Assert.Single(result.Value!.Lines);
        Assert.NotNull(line.Sale);
        SaleDto sale = line.Sale;
        Assert.Equal("eBay", sale.Platform);
        Assert.Equal(120m, sale.SoldPrice);
        Assert.Equal(10m, sale.ShippingCharged);
        Assert.Equal(18m, sale.PlatformFees + sale.PaymentFees);
        Assert.Equal(72m, sale.NetProfit);
        // The sale carries its real Order id so the finance table can link straight back to the order.
        Assert.Equal(order.Id, sale.OrderId);
    }

    [Fact]
    public async Task Order_detail_rolls_up_refunds_across_its_lines()
    {
        var (db, org, user) = NewContext(nameof(Order_detail_rolls_up_refunds_across_its_lines));
        await using var _ = db;
        var (itemId, platformOrderId, lineId) = await SeedEbayOrder(db, org, user, "EB-RF", "buyer_bob");
        await NewPlatformOrders(db, org, user).Reconcile(platformOrderId,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(lineId, itemId)]), CancellationToken.None);

        Order order = await db.Orders.SingleAsync();
        Sale sale = await db.Sales.SingleAsync(item => item.OrderId == order.Id);
        db.Refunds.Add(new Refund
        {
            OrganizationId = org,
            SaleId = sale.Id,
            Type = RefundType.Partial,
            ItemAmount = 20m,
            ShippingRefunded = 5m,
            Reason = "Buyer kept it, partial refund",
            RecordedByUserId = user,
        });
        await db.SaveChangesAsync();

        ActionResult<OrderDetailDto> result = await NewOrders(db, org, user).Get(order.Id, CancellationToken.None);

        // Buyer money refunded rolls up to the order: 20 item + 5 shipping.
        Assert.Equal(25m, result.Value!.TotalRefunded);
    }

    [Fact]
    public async Task Deleting_an_order_removes_it_and_its_lines_but_preserves_the_sale()
    {
        var (db, org, user) = NewContext(nameof(Deleting_an_order_removes_it_and_its_lines_but_preserves_the_sale));
        await using var _ = db;
        var (itemId, platformOrderId, lineId) = await SeedEbayOrder(db, org, user, "EB-DEL-1", "buyer_bob");

        await NewPlatformOrders(db, org, user).Reconcile(platformOrderId,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(lineId, itemId)]), CancellationToken.None);

        Order order = await db.Orders.SingleAsync();
        Guid saleId = (await db.Sales.SingleAsync(sale => sale.OrderId == order.Id)).Id;

        IActionResult result = await NewOrders(db, org, user).Delete(order.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await db.Orders.AnyAsync(item => item.Id == order.Id));
        Assert.False(await db.OrderLines.AnyAsync(line => line.OrderId == order.Id));
        // The sale survives the order — financial history is never destroyed, just detached.
        Sale survivor = await db.Sales.SingleAsync(sale => sale.Id == saleId);
        Assert.Null(survivor.OrderId);
    }

    [Fact]
    public async Task Deleting_a_missing_order_returns_not_found()
    {
        var (db, org, user) = NewContext(nameof(Deleting_a_missing_order_returns_not_found));
        await using var _ = db;

        IActionResult result = await NewOrders(db, org, user).Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task A_marketplace_buyer_becomes_a_customer_with_a_channel_identity()
    {
        var (db, org, user) = NewContext(nameof(A_marketplace_buyer_becomes_a_customer_with_a_channel_identity));
        await using var _ = db;
        var (itemId, platformOrderId, lineId) = await SeedEbayOrder(db, org, user, "EB-ORDER-2", "buyer_bob");

        await NewPlatformOrders(db, org, user).Reconcile(platformOrderId,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(lineId, itemId)]), CancellationToken.None);

        var customer = Assert.Single(await db.Customers.Include(c => c.ChannelIdentities).ToListAsync());
        Assert.Null(customer.Email);                    // no email — identified by handle
        var identity = Assert.Single(customer.ChannelIdentities);
        Assert.Equal("eBay", identity.Channel);
        Assert.Equal("buyer_bob", identity.ExternalUserId);

        var order = await db.Orders.SingleAsync();
        Assert.Equal(customer.Id, order.CustomerId);
    }

    [Fact]
    public async Task The_same_buyer_on_a_second_order_is_recognised_not_duplicated()
    {
        var (db, org, user) = NewContext(nameof(The_same_buyer_on_a_second_order_is_recognised_not_duplicated));
        await using var _ = db;

        var first = await SeedEbayOrder(db, org, user, "EB-ORDER-3", "repeat_buyer");
        await NewPlatformOrders(db, org, user).Reconcile(first.PlatformOrderId,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(first.LineId, first.ItemId)]), CancellationToken.None);

        var second = await SeedEbayOrder(db, org, user, "EB-ORDER-4", "repeat_buyer");
        await NewPlatformOrders(db, org, user).Reconcile(second.PlatformOrderId,
            new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(second.LineId, second.ItemId)]), CancellationToken.None);

        Assert.Equal(1, await db.Customers.CountAsync());                 // one human
        Assert.Equal(1, await db.CustomerChannelIdentities.CountAsync()); // one identity
        Assert.Equal(2, await db.Orders.CountAsync());                    // two orders
    }

    [Fact]
    public async Task Reconciling_the_same_platform_order_twice_reuses_the_order_header()
    {
        var (db, org, user) = NewContext(nameof(Reconciling_the_same_platform_order_twice_reuses_the_order_header));
        await using var _ = db;
        var currentUser = new StubCurrentUser(org, user);
        var assembler = new OrderAssembler(db, currentUser);

        var a = await assembler.GetOrCreateAsync("eBay", "DUP-1", null, "bob", "Bob", DateTimeOffset.UtcNow, null, 10m, 1m, 0m, CancellationToken.None);
        await db.SaveChangesAsync();
        var b = await assembler.GetOrCreateAsync("eBay", "DUP-1", null, "bob", "Bob", DateTimeOffset.UtcNow, null, 10m, 1m, 0m, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Storefront_orders_are_just_another_channel()
    {
        var (db, org, user) = NewContext(nameof(Storefront_orders_are_just_another_channel));
        await using var _ = db;
        db.Orders.Add(new Order
        {
            OrganizationId = org,
            OrderNumber = "SF-1",
            Email = "shopper@example.com",
            // Channel defaults to Storefront — no special-casing anywhere.
        });
        await db.SaveChangesAsync();

        var order = await db.Orders.SingleAsync();
        Assert.Equal("Storefront", order.Channel);
        Assert.Null(order.ExternalOrderId);
    }

    [Fact]
    public async Task Manually_recorded_sale_creates_a_real_order_with_order_line()
    {
        var (db, org, user) = NewContext(nameof(Manually_recorded_sale_creates_a_real_order_with_order_line));
        await using var _ = db;
        InventoryItem item = await SeedItem(db, org, "Wii Sports");

        ActionResult<SaleDto> recorded = await NewSales(db, org, user).Record(new RecordSaleRequest(
            item.Id, null, "eBay", 28m, 3m, 1m, 4m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-1), null,
            SalesTaxCollected: 2m,
            ShippingCharged: 5m,
            ExternalOrderId: "EB-MANUAL-1",
            BuyerReference: "buyer_bob",
            BuyerName: "Bob Buyer",
            ShippingCarrier: "USPS",
            TrackingNumber: "9400TRACK"), CancellationToken.None);

        SaleDto recordedSale = Assert.IsType<CreatedAtActionResult>(recorded.Result).Value as SaleDto
            ?? throw new InvalidOperationException("Expected sale DTO.");
        Order order = Assert.Single(await db.Orders.Include(order => order.Lines).ToListAsync());
        Assert.Equal("eBay", order.Channel);
        Assert.Equal("EB-MANUAL-1", order.ExternalOrderId);
        Assert.Equal("9400TRACK", order.TrackingNumber);
        Assert.Equal(35m, order.GrandTotal); // 28 item + 5 shipping + 2 tax
        Assert.Equal(order.Id, recordedSale.SaleGroupId);
        Assert.Single(order.Lines);
    }

    [Fact]
    public async Task Deleting_the_only_sale_on_a_marketplace_order_prunes_the_phantom_order()
    {
        var (db, org, user) = NewContext(nameof(Deleting_the_only_sale_on_a_marketplace_order_prunes_the_phantom_order));
        await using var _ = db;
        InventoryItem item = await SeedItem(db, org, "Wii Sports");
        SalesController sales = NewSales(db, org, user);

        ActionResult<SaleDto> recorded = await sales.Record(new RecordSaleRequest(
            item.Id, null, "eBay", 28m, 3m, 1m, 4m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-1), null,
            ShippingCharged: 5m, ExternalOrderId: "EB-PHANTOM-1", BuyerReference: "buyer_bob"), CancellationToken.None);
        SaleDto sale = Assert.IsType<CreatedAtActionResult>(recorded.Result).Value as SaleDto
            ?? throw new InvalidOperationException("Expected sale DTO.");
        Assert.Single(await db.Orders.ToListAsync());

        await sales.Delete(sale.Id, CancellationToken.None);

        // The order lost its only sale, so it must not linger as an empty phantom.
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.OrderLines.ToListAsync());
    }

    [Fact]
    public async Task Manual_sales_with_same_external_order_number_reuse_one_order()
    {
        var (db, org, user) = NewContext(nameof(Manual_sales_with_same_external_order_number_reuse_one_order));
        await using var _ = db;
        InventoryItem first = await SeedItem(db, org, "Game A");
        InventoryItem second = await SeedItem(db, org, "Game B");
        SalesController sales = NewSales(db, org, user);

        await sales.Record(new RecordSaleRequest(first.Id, null, "eBay", 20m, 2m, 0m, 3m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-2), null, ShippingCharged: 4m, ExternalOrderId: "EB-BUNDLE-1"), CancellationToken.None);
        await sales.Record(new RecordSaleRequest(second.Id, null, "eBay", 30m, 3m, 0m, 0m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-2), null, ShippingCharged: 0m, ExternalOrderId: "EB-BUNDLE-1"), CancellationToken.None);

        Order order = Assert.Single(await db.Orders.Include(order => order.Lines).ToListAsync());
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(54m, order.GrandTotal);
        Assert.Equal(2, await db.Sales.CountAsync(sale => sale.OrderId == order.Id));
    }

    [Fact]
    public async Task Merging_orders_consolidates_lines_and_sales_and_removes_the_empty_order()
    {
        var (db, org, user) = NewContext(nameof(Merging_orders_consolidates_lines_and_sales_and_removes_the_empty_order));
        await using var _ = db;
        InventoryItem a = await SeedItem(db, org, "Game A");
        InventoryItem b = await SeedItem(db, org, "Game B");
        SalesController sales = NewSales(db, org, user);

        // Two sales the buyer made together but recorded under separate order numbers → two separate orders.
        await sales.Record(new RecordSaleRequest(a.Id, null, "eBay", 20m, 2m, 0m, 3m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-3), null, ShippingCharged: 4m, ExternalOrderId: "EB-A"), CancellationToken.None);
        await sales.Record(new RecordSaleRequest(b.Id, null, "eBay", 30m, 3m, 0m, 0m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-2), null, ShippingCharged: 5m, ExternalOrderId: "EB-B"), CancellationToken.None);
        List<Guid> ids = (await db.Orders.ToListAsync()).Select(order => order.Id).ToList();
        Assert.Equal(2, ids.Count);

        ActionResult<OrderDetailDto> result = await NewOrders(db, org, user).Merge(
            new MergeOrdersRequest(ids), CancellationToken.None);

        // One order remains, holding both lines and both sales, with summed totals.
        Order survivor = Assert.Single(await db.Orders.ToListAsync());
        Assert.Equal(survivor.Id, result.Value!.Id);
        Assert.Equal(2, await db.OrderLines.CountAsync(line => line.OrderId == survivor.Id));
        Assert.Equal(2, await db.Sales.CountAsync(sale => sale.OrderId == survivor.Id));
        Assert.Equal(50m, survivor.Subtotal);       // 20 + 30
        Assert.Equal(9m, survivor.ShippingTotal);   // 4 + 5
        Assert.Equal(59m, survivor.GrandTotal);
    }

    [Fact]
    public async Task Merging_orders_on_different_channels_is_rejected()
    {
        var (db, org, user) = NewContext(nameof(Merging_orders_on_different_channels_is_rejected));
        await using var _ = db;
        InventoryItem a = await SeedItem(db, org, "Game A");
        InventoryItem b = await SeedItem(db, org, "Game B");
        SalesController sales = NewSales(db, org, user);

        await sales.Record(new RecordSaleRequest(a.Id, null, "eBay", 20m, 2m, 0m, 0m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-2), null, ExternalOrderId: "EB-X"), CancellationToken.None);
        await sales.Record(new RecordSaleRequest(b.Id, null, "Poshmark", 30m, 3m, 0m, 0m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-2), null, ExternalOrderId: "PM-X"), CancellationToken.None);
        List<Guid> ids = (await db.Orders.ToListAsync()).Select(order => order.Id).ToList();

        ActionResult<OrderDetailDto> result = await NewOrders(db, org, user).Merge(
            new MergeOrdersRequest(ids), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(2, await db.Orders.CountAsync()); // nothing merged
    }

    [Fact]
    public async Task Manual_sales_with_same_tracking_number_reuse_one_order_when_order_number_is_missing()
    {
        var (db, org, user) = NewContext(nameof(Manual_sales_with_same_tracking_number_reuse_one_order_when_order_number_is_missing));
        await using var _ = db;
        InventoryItem first = await SeedItem(db, org, "Card A");
        InventoryItem second = await SeedItem(db, org, "Card B");
        SalesController sales = NewSales(db, org, user);

        await sales.Record(new RecordSaleRequest(first.Id, null, "eBay", 12m, 1m, 0m, 0m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-1), null, TrackingNumber: "TRACK-SAME"), CancellationToken.None);
        await sales.Record(new RecordSaleRequest(second.Id, null, "eBay", 18m, 1m, 0m, 0m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-1), null, TrackingNumber: "TRACK-SAME"), CancellationToken.None);

        Order order = Assert.Single(await db.Orders.Include(order => order.Lines).ToListAsync());
        Assert.Equal("TRACK-SAME", order.TrackingNumber);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(30m, order.GrandTotal);
    }
}
