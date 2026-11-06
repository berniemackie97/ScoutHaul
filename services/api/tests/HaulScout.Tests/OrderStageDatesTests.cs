using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
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
/// The stage timeline is history. Marking a stage records that stage, once, on the day it happened; a stage nobody
/// recorded stays unrecorded; and a marketplace saying "delivered" reaches the order without anyone clicking.
/// Shaped on eBay order 19-15187-69578: shipped Sep 24, delivered Sep 29, marked delivered by hand on Nov 5.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrderStageDatesTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Placed = new(2026, 9, 23, 17, 25, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Shipped = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Delivered = new(2026, 9, 29, 17, 30, 0, TimeSpan.Zero);

    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static async Task<Order> SeedMarketplaceOrder(HaulScoutDbContext db, Guid org, bool localPickup = false, string channel = "eBay")
    {
        Order order = new()
        {
            OrganizationId = org,
            Channel = channel,
            ExternalOrderId = channel == Order.StorefrontChannel ? null : "19-15187-69578",
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..16],
            Status = localPickup ? OrderStatus.Paid : OrderStatus.Shipped,
            PaymentStatus = PaymentStatus.Paid,
            FulfillmentStatus = localPickup ? FulfillmentStatus.Unfulfilled : FulfillmentStatus.Fulfilled,
            CreatedAt = Placed,
            PaidAt = Placed,
            PackedAt = localPickup ? Placed.AddHours(2) : null,
            ShippedAt = localPickup ? null : Shipped,
            IsLocalPickup = localPickup,
            ShippingCarrier = localPickup ? null : "USPS",
            TrackingNumber = localPickup ? null : "9400108106245604839754",
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static OrdersController Controller(HaulScoutDbContext db, Guid org, Guid user) =>
        new(OrdersServiceFactory.Create(db, new StubCurrentUser(org, user)));

    private static UpdateOrderTrackingRequest MarkDelivered(DateTimeOffset? at = null) =>
        new(null, null, null, null, at, MarkShipped: false, MarkDelivered: true);

    [Fact]
    public async Task Marking_delivered_keeps_the_shipped_date_and_invents_no_packed_date()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        Order order = await SeedMarketplaceOrder(db, org);

        await Controller(db, org, user).UpdateTracking(order.Id, MarkDelivered(Delivered), CancellationToken.None);

        db.ChangeTracker.Clear();
        Order stored = await db.Orders.SingleAsync(entry => entry.Id == order.Id);
        Assert.Equal(Shipped, stored.ShippedAt);
        Assert.Null(stored.PackedAt);
        Assert.Equal(Delivered, stored.DeliveredAt);
        Assert.Equal(OrderStatus.Delivered, stored.Status);
        Assert.Equal("9400108106245604839754", stored.TrackingNumber);
    }

    [Fact]
    public async Task Marking_delivered_without_a_date_dates_only_the_delivery()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        Order order = await SeedMarketplaceOrder(db, org);
        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await Controller(db, org, user).UpdateTracking(order.Id, MarkDelivered(), CancellationToken.None);

        db.ChangeTracker.Clear();
        Order stored = await db.Orders.SingleAsync(entry => entry.Id == order.Id);
        Assert.Equal(Shipped, stored.ShippedAt);
        Assert.Null(stored.PackedAt);
        Assert.True(stored.DeliveredAt >= before);
    }

    [Fact]
    public async Task Marking_delivered_twice_keeps_the_first_date()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        Order order = await SeedMarketplaceOrder(db, org);
        OrdersController controller = Controller(db, org, user);

        await controller.UpdateTracking(order.Id, MarkDelivered(Delivered), CancellationToken.None);
        await controller.UpdateTracking(order.Id, MarkDelivered(), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(Delivered, (await db.Orders.SingleAsync(entry => entry.Id == order.Id)).DeliveredAt);
    }

    [Fact]
    public async Task A_delivery_dated_before_the_shipment_is_refused()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        Order order = await SeedMarketplaceOrder(db, org);

        await Controller(db, org, user).UpdateTracking(order.Id, MarkDelivered(Shipped.AddDays(-1)), CancellationToken.None);

        db.ChangeTracker.Clear();
        Order stored = await db.Orders.SingleAsync(entry => entry.Id == order.Id);
        Assert.Null(stored.DeliveredAt);
        Assert.Equal(OrderStatus.Shipped, stored.Status);
    }

    [Fact]
    public async Task Our_own_shipment_cannot_be_delivered_before_it_ships()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        Order order = await SeedMarketplaceOrder(db, org, channel: Order.StorefrontChannel);
        order.ShippedAt = null;
        order.Status = OrderStatus.Paid;
        await db.SaveChangesAsync();

        await Controller(db, org, user).UpdateTracking(order.Id, MarkDelivered(Delivered), CancellationToken.None);

        db.ChangeTracker.Clear();
        Order stored = await db.Orders.SingleAsync(entry => entry.Id == order.Id);
        Assert.Null(stored.DeliveredAt);
        Assert.Null(stored.ShippedAt);
    }

    [Fact]
    public async Task A_collected_pickup_was_never_shipped()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        Order order = await SeedMarketplaceOrder(db, org, localPickup: true, channel: Order.StorefrontChannel);
        DateTimeOffset packed = order.PackedAt!.Value;

        await Controller(db, org, user).UpdateTracking(order.Id, MarkDelivered(), CancellationToken.None);

        db.ChangeTracker.Clear();
        Order stored = await db.Orders.SingleAsync(entry => entry.Id == order.Id);
        Assert.NotNull(stored.DeliveredAt);
        Assert.Null(stored.ShippedAt);
        Assert.Equal(packed, stored.PackedAt);
    }

    [Fact]
    public async Task The_order_offers_the_marketplace_delivery_date_to_mark_it_delivered_with()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        Order order = await SeedMarketplaceOrder(db, org);
        db.PlatformOrders.Add(new PlatformOrder
        {
            OrganizationId = org,
            Platform = "eBay",
            ExternalOrderId = "19-15187-69578",
            OrderedAt = Placed,
            ShippedAt = Shipped,
            DeliveredAt = Delivered,
            FulfillmentStage = PlatformFulfillmentStage.Delivered,
        });
        await db.SaveChangesAsync();

        OrderDetailDto? detail = (await Controller(db, org, user).Get(order.Id, CancellationToken.None)).Value;

        Assert.Equal(Delivered, detail?.TrackingDeliveredAt);
    }

    [Fact]
    public async Task A_scan_whose_only_news_is_delivery_reaches_the_order()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        StubCurrentUser currentUser = new(org, user);
        EfPlatformOrderStore store = new(db, new OrderAssembler(db, currentUser));
        PlatformOrderSuggestionService suggestions = new(new EfPlatformOrderSuggestionStore(db, currentUser));
        PlatformOrderWorkflowService workflow = new(store, currentUser, suggestions);
        PlatformOrderIngestionService ingestion = new(store, currentUser, suggestions, workflow, new PlatformOrderAutoMatchService(suggestions, workflow, store, currentUser));

        IngestPlatformOrderInput Scan(PlatformFulfillmentStage stage, DateTimeOffset? deliveredAt) => new(
            "19-15187-69578", Placed, 16.99m, 7.69m, 1.54m, 3.73m,
            [new IngestPlatformOrderLineInput("Island Winds Caribbean Joe Zip Jacket", 16.99m, 1, "377000000001")],
            Platform: "eBay",
            Carrier: "USPS",
            TrackingNumber: "9400108106245604839754",
            ShippedAt: Shipped,
            DeliveredAt: deliveredAt,
            FulfillmentStage: stage);

        await ingestion.IngestAsync("eBay", [Scan(PlatformFulfillmentStage.Shipped, null)], null, CancellationToken.None);
        PlatformOrder platformOrder = await db.PlatformOrders.Include(entry => entry.Lines).SingleAsync();
        if (platformOrder.Status != PlatformOrderStatus.Reconciled)
        {
            AppResult<PlatformOrderReconcileResultDto> reconciled = await workflow.ReconcileAsync(
                platformOrder.Id,
                new ReconcilePlatformOrderRequest([new ReconcilePlatformOrderLineMap(platformOrder.Lines.Single().Id, CreateItem: true)]),
                CancellationToken.None);
            Assert.Equal(AppResultStatus.Ok, reconciled.Status);
        }

        Order before = await db.Orders.AsNoTracking().SingleAsync();
        Assert.Null(before.DeliveredAt);

        await ingestion.IngestAsync("eBay", [Scan(PlatformFulfillmentStage.Delivered, Delivered)], null, CancellationToken.None);

        db.ChangeTracker.Clear();
        Order after = await db.Orders.SingleAsync();
        Assert.Equal(Delivered, after.DeliveredAt);
        Assert.Equal(Shipped, after.ShippedAt);
        Assert.Null(after.PackedAt);
        Assert.Equal(OrderStatus.Delivered, after.Status);
    }
}
