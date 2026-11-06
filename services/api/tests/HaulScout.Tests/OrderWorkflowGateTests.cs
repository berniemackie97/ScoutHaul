using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// The order's stage and the actions it allows come from <see cref="OrderWorkflow"/>, so every surface
/// agrees. <see cref="OrderWorkflowTests"/> covers the rules themselves; this covers the wiring — that the
/// API answers with the stage, refuses a transition the domain refuses, and explains why.
/// </summary>
public class OrderWorkflowGateTests
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
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static async Task<Order> SeedOrder(
        HaulScoutDbContext db,
        Guid org,
        PaymentStatus payment = PaymentStatus.Paid,
        bool localPickup = false,
        bool withAddress = true,
        string channel = "Storefront")
    {
        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", PurchasePrice = 40m, Status = InventoryStatus.Sold };
        db.InventoryItems.Add(item);
        Order order = new()
        {
            OrganizationId = org,
            Channel = channel,
            OrderNumber = "ORD-1",
            Status = payment == PaymentStatus.Paid ? OrderStatus.Paid : OrderStatus.Pending,
            PaymentStatus = payment,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            IsLocalPickup = localPickup,
            ShipToLine1 = withAddress ? "1 Main St" : null,
        };
        db.Orders.Add(order);
        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = channel,
            SoldPrice = 100m,
            CostOfGoods = 40m,
            Status = SaleStatus.Recorded,
            Order = order,
            InventoryItem = item,
        };
        sale.RecomputeNet();
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return order;
    }

    private static OrdersController NewController(HaulScoutDbContext db, StubCurrentUser currentUser) =>
        new(OrdersServiceFactory.Create(db, currentUser));

    [Fact]
    public async Task The_order_reports_the_stage_it_is_actually_in()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(The_order_reports_the_stage_it_is_actually_in));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;

        Assert.NotNull(detail?.Workflow);
        Assert.Equal(nameof(OrderWorkflowStage.ToPack), detail.Workflow.Stage);
        Assert.Contains(nameof(OrderWorkflowAction.MarkPacked), detail.Workflow.AvailableActions);
        // Shipping is not offered before packing.
        Assert.DoesNotContain(nameof(OrderWorkflowAction.Ship), detail.Workflow.AvailableActions);
    }

    [Fact]
    public async Task An_unpaid_order_cannot_be_packed_and_says_why()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(An_unpaid_order_cannot_be_packed_and_says_why));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org, payment: PaymentStatus.Pending);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(
            order.Id,
            new UpdateOrderTrackingRequest(null, null, null, null, null, MarkShipped: false, MarkDelivered: false, MarkPacked: true),
            CancellationToken.None);

        // Nothing was packed, and the stage still says what is waiting.
        Assert.Null((await db.Orders.SingleAsync()).PackedAt);
        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;
        Assert.Equal(nameof(OrderWorkflowStage.AwaitingPayment), detail?.Workflow?.Stage);
        Assert.Contains(detail!.Workflow!.Blockers, blocker => blocker.Contains("payment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_shipping_order_with_nowhere_to_send_it_cannot_be_packed()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_shipping_order_with_nowhere_to_send_it_cannot_be_packed));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org, withAddress: false);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(
            order.Id,
            new UpdateOrderTrackingRequest(null, null, null, null, null, MarkShipped: false, MarkDelivered: false, MarkPacked: true),
            CancellationToken.None);

        Assert.Null((await db.Orders.SingleAsync()).PackedAt);
        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;
        Assert.Contains(detail!.Workflow!.Blockers, blocker => blocker.Contains("address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Packing_a_paid_order_moves_it_to_the_shipping_queue()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Packing_a_paid_order_moves_it_to_the_shipping_queue));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(
            order.Id,
            new UpdateOrderTrackingRequest(null, null, null, null, null, MarkShipped: false, MarkDelivered: false, MarkPacked: true),
            CancellationToken.None);

        Assert.NotNull((await db.Orders.SingleAsync()).PackedAt);
        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;
        Assert.Equal(nameof(OrderWorkflowStage.ToShip), detail?.Workflow?.Stage);
        // Shipping still wants a carrier and a tracking number, and says so rather than silently refusing.
        Assert.DoesNotContain(nameof(OrderWorkflowAction.Ship), detail!.Workflow!.AvailableActions);
        Assert.Contains(detail.Workflow.Blockers, blocker => blocker.Contains("carrier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_local_pickup_order_waits_for_collection_rather_than_a_carrier()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_local_pickup_order_waits_for_collection_rather_than_a_carrier));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org, localPickup: true, withAddress: false);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(
            order.Id,
            new UpdateOrderTrackingRequest(null, null, null, null, null, MarkShipped: false, MarkDelivered: false, MarkPacked: true),
            CancellationToken.None);

        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;
        // No shipping address is needed to pack something the buyer collects.
        Assert.NotNull((await db.Orders.SingleAsync()).PackedAt);
        Assert.Equal(nameof(OrderWorkflowStage.ReadyForPickup), detail?.Workflow?.Stage);
        Assert.Contains(nameof(OrderWorkflowAction.MarkPickedUp), detail!.Workflow!.AvailableActions);
    }

    [Fact]
    public async Task Recording_a_shipment_moves_on_without_inventing_a_packed_date()
    {
        // Marketplace orders arrive already shipped. The stage must not stall behind a packing step
        // nobody performed in this system, and must not date that step either.
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Recording_a_shipment_moves_on_without_inventing_a_packed_date));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(
            order.Id,
            new UpdateOrderTrackingRequest("USPS", "9400abc", null, null, null, MarkShipped: true, MarkDelivered: false),
            CancellationToken.None);

        Order stored = await db.Orders.SingleAsync();
        Assert.NotNull(stored.ShippedAt);
        Assert.Null(stored.PackedAt);
        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;
        Assert.Equal(nameof(OrderWorkflowStage.InTransit), detail?.Workflow?.Stage);
    }

    [Fact]
    public async Task Packing_twice_keeps_the_first_time()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Packing_twice_keeps_the_first_time));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));
        UpdateOrderTrackingRequest pack = new(null, null, null, null, null, MarkShipped: false, MarkDelivered: false, MarkPacked: true);

        await controller.UpdateTracking(order.Id, pack, CancellationToken.None);
        DateTimeOffset? first = (await db.Orders.SingleAsync()).PackedAt;
        await controller.UpdateTracking(order.Id, pack, CancellationToken.None);

        Assert.Equal(first, (await db.Orders.SingleAsync()).PackedAt);
    }

    [Fact]
    public async Task A_refused_request_writes_nothing_at_all()
    {
        // Packing and the timestamp checks are one request. If a later check refuses, the earlier
        // half must not have been applied — a half-saved order is worse than a rejected one.
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_refused_request_writes_nothing_at_all));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(
            order.Id,
            new UpdateOrderTrackingRequest(
                "USPS",
                "9400abc",
                null,
                ShippedAt: DateTimeOffset.UtcNow.AddDays(2),   // refused: in the future
                null,
                MarkShipped: false,
                MarkDelivered: false,
                MarkPacked: true),
            CancellationToken.None);

        Order stored = await db.Orders.SingleAsync();
        Assert.Null(stored.PackedAt);
        Assert.Null(stored.ShippedAt);
        Assert.Null(stored.ShippingCarrier);
    }

    [Fact]
    public async Task An_order_with_a_return_in_flight_needs_attention_wherever_it_got_to()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(An_order_with_a_return_in_flight_needs_attention_wherever_it_got_to));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        Sale sale = await db.Sales.SingleAsync();
        db.Returns.Add(Return.Open(org, sale, "Not as described", null, user, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;

        Assert.Equal(nameof(OrderWorkflowStage.NeedsAttention), detail?.Workflow?.Stage);
    }

    private static UpdateOrderTrackingRequest Step(bool packed = false, bool shipped = false, bool delivered = false) =>
        new(null, null, null, null, null, MarkShipped: shipped, MarkDelivered: delivered, MarkPacked: packed);

    [Fact]
    public async Task A_marketplace_order_packs_and_ships_without_an_address_or_tracking()
    {
        // Depop and Poshmark keep the address and print the label; demanding either here stranded the order in To pack.
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_marketplace_order_packs_and_ships_without_an_address_or_tracking));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org, withAddress: false, channel: "Depop");
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(order.Id, Step(packed: true), CancellationToken.None);
        Assert.NotNull((await db.Orders.SingleAsync()).PackedAt);

        await controller.UpdateTracking(order.Id, Step(shipped: true), CancellationToken.None);
        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;
        Assert.Equal(nameof(OrderWorkflowStage.InTransit), detail?.Workflow?.Stage);
    }

    [Fact]
    public async Task A_storefront_order_still_needs_tracking_to_ship()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_storefront_order_still_needs_tracking_to_ship));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(order.Id, Step(packed: true), CancellationToken.None);
        await controller.UpdateTracking(order.Id, Step(shipped: true), CancellationToken.None);

        Assert.Null((await db.Orders.SingleAsync()).ShippedAt);
    }

    [Fact]
    public async Task A_bare_step_keeps_the_tracking_already_recorded()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_bare_step_keeps_the_tracking_already_recorded));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));
        await controller.UpdateTracking(
            order.Id,
            new UpdateOrderTrackingRequest("USPS", "9400abc", null, null, null, MarkShipped: true, MarkDelivered: false),
            CancellationToken.None);

        await controller.UpdateTracking(order.Id, Step(delivered: true), CancellationToken.None);

        Order stored = await db.Orders.SingleAsync();
        Assert.Equal("9400abc", stored.TrackingNumber);
        Assert.NotNull(stored.DeliveredAt);
    }

    [Fact]
    public async Task Shipping_and_delivery_cannot_be_set_through_the_status_door()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Shipping_and_delivery_cannot_be_set_through_the_status_door));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org, payment: PaymentStatus.Pending);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateStatus(order.Id, new UpdateOrderStatusRequest(OrderStatus.Delivered), CancellationToken.None);

        Order stored = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Pending, stored.Status);
        Assert.Null(stored.DeliveredAt);
    }

    [Fact]
    public async Task A_delivered_order_cannot_be_cancelled()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_delivered_order_cannot_be_cancelled));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org, channel: "eBay");
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));
        await controller.UpdateTracking(order.Id, Step(delivered: true), CancellationToken.None);

        await controller.UpdateStatus(order.Id, new UpdateOrderStatusRequest(OrderStatus.Cancelled), CancellationToken.None);

        Assert.Equal(OrderStatus.Delivered, (await db.Orders.SingleAsync()).Status);
        Assert.NotEqual(SaleStatus.Cancelled, (await db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancelling_an_order_reverses_its_sales_and_restocks_the_item()
    {
        // Before, the order read Cancelled while its sale stayed realized revenue and the item stayed sold.
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Cancelling_an_order_reverses_its_sales_and_restocks_the_item));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateStatus(order.Id, new UpdateOrderStatusRequest(OrderStatus.Cancelled), CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(SaleStatus.Cancelled, (await db.Sales.SingleAsync()).Status);
        Assert.NotEqual(InventoryStatus.Sold, (await db.InventoryItems.SingleAsync()).Status);
    }

    [Fact]
    public async Task The_order_margin_uses_the_sales_cost_of_goods()
    {
        // A cost entered after the sale updates the sale, not the line snapshot; the margin read the stale snapshot.
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(The_order_margin_uses_the_sales_cost_of_goods));
        await using HaulScoutDbContext _ = db;
        Order order = await SeedOrder(db, org);
        db.OrderLines.Add(new OrderLine
        {
            OrganizationId = org,
            OrderId = order.Id,
            InventoryItemId = (await db.InventoryItems.SingleAsync()).Id,
            TitleSnapshot = "Brass Lamp",
            UnitPrice = 100m,
            LineTotal = 100m,
            Quantity = 1,
            UnitCostSnapshot = 0m,
        });
        order.Subtotal = 100m;
        await db.SaveChangesAsync();
        OrdersController controller = NewController(db, new StubCurrentUser(org, user));

        OrderDetailDto? detail = (await controller.Get(order.Id, CancellationToken.None)).Value;

        Assert.Equal(40m, detail?.CostOfGoods);
        Assert.Equal(60m, detail?.GrossMargin);
    }
}
