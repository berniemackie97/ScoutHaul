using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Fulfillment has one source of truth: the Order. Marking it happens once, on the order, and propagates to the
/// sale lines. Editing a sale afterward (which re-syncs the order's money) must NOT reset the fulfillment the
/// operator set — that "un-deliver" overwrite was the reason the same order had to be marked in three places.
/// </summary>
public class OrderFulfillmentSsotTests
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

    // An eBay order with one sale line, freshly reconciled (Unfulfilled, sale Recorded).
    private static async Task<(Order Order, Sale Sale)> SeedOrderWithSale(HaulScoutDbContext db, Guid org)
    {
        var item = new InventoryItem { OrganizationId = org, Title = "Brass Lamp", PurchasePrice = 40m, Status = InventoryStatus.Sold };
        db.InventoryItems.Add(item);
        var order = new Order { OrganizationId = org, Channel = "eBay", ExternalOrderId = "EB-1", OrderNumber = "EB-1", Status = OrderStatus.Paid, PaymentStatus = PaymentStatus.Paid, FulfillmentStatus = FulfillmentStatus.Unfulfilled };
        db.Orders.Add(order);
        var sale = new Sale { OrganizationId = org, InventoryItemId = item.Id, Platform = "eBay", SoldPrice = 100m, CostOfGoods = 40m, Status = SaleStatus.Recorded, Order = order, InventoryItem = item };
        sale.RecomputeNet();
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return (order, sale);
    }

    private static OrdersController NewController(HaulScoutDbContext db, StubCurrentUser currentUser) =>
        new(new OrdersWorkflowService(db, currentUser, new OrderAssembler(db, currentUser)));

    [Fact]
    public async Task Marking_the_order_delivered_propagates_to_its_sales()
    {
        var (db, org, user) = NewContext(nameof(Marking_the_order_delivered_propagates_to_its_sales));
        await using var _ = db;
        var (order, sale) = await SeedOrderWithSale(db, org);
        var controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(order.Id, new UpdateOrderTrackingRequest(
            "USPS", "9400abc", null, null, null, MarkShipped: false, MarkDelivered: true), CancellationToken.None);

        var stored = await db.Orders.SingleAsync();
        Assert.Equal(FulfillmentStatus.Fulfilled, stored.FulfillmentStatus);
        Assert.Equal(OrderStatus.Delivered, stored.Status);
        Assert.NotNull(stored.DeliveredAt);
        // The sale line mirrors the order — one source of truth, no separate sale-fulfillment action.
        Assert.Equal(SaleStatus.Delivered, (await db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task Saving_a_shipped_timestamp_marks_the_order_shipped_without_a_separate_button()
    {
        var (db, org, user) = NewContext(nameof(Saving_a_shipped_timestamp_marks_the_order_shipped_without_a_separate_button));
        await using var _ = db;
        var (order, sale) = await SeedOrderWithSale(db, org);
        var shippedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(order.Id, new UpdateOrderTrackingRequest(
            "USPS", "9400abc", null, shippedAt, null, MarkShipped: false, MarkDelivered: false), CancellationToken.None);

        var stored = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Shipped, stored.Status);
        Assert.Equal(FulfillmentStatus.Fulfilled, stored.FulfillmentStatus);
        Assert.Equal(shippedAt, stored.ShippedAt);
        Assert.Equal(SaleStatus.Shipped, (await db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task Saving_a_delivered_timestamp_marks_delivered_and_backfills_shipped_timestamp()
    {
        var (db, org, user) = NewContext(nameof(Saving_a_delivered_timestamp_marks_delivered_and_backfills_shipped_timestamp));
        await using var _ = db;
        var (order, sale) = await SeedOrderWithSale(db, org);
        var deliveredAt = DateTimeOffset.UtcNow.AddHours(-1);
        var controller = NewController(db, new StubCurrentUser(org, user));

        await controller.UpdateTracking(order.Id, new UpdateOrderTrackingRequest(
            "USPS", "9400abc", null, null, deliveredAt, MarkShipped: false, MarkDelivered: false), CancellationToken.None);

        var stored = await db.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Delivered, stored.Status);
        Assert.Equal(FulfillmentStatus.Fulfilled, stored.FulfillmentStatus);
        Assert.Equal(deliveredAt, stored.ShippedAt);
        Assert.Equal(deliveredAt, stored.DeliveredAt);
        Assert.Equal(SaleStatus.Delivered, (await db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task Editing_a_sale_after_delivery_does_not_un_deliver_the_order()
    {
        var (db, org, user) = NewContext(nameof(Editing_a_sale_after_delivery_does_not_un_deliver_the_order));
        await using var _ = db;
        var (order, sale) = await SeedOrderWithSale(db, org);
        var currentUser = new StubCurrentUser(org, user);
        await NewController(db, currentUser).UpdateTracking(order.Id,
            new UpdateOrderTrackingRequest("USPS", "9400abc", null, null, null, false, MarkDelivered: true), CancellationToken.None);

        // Re-sync the order's money from its sales (what a sale edit / refund / reconcile triggers).
        var assembler = new OrderAssembler(db, currentUser);
        var reloadedOrder = await db.Orders.SingleAsync();
        var reloadedSale = await db.Sales.SingleAsync();
        reloadedSale.SoldPrice = 120m; // an edit
        await assembler.SyncSalesOrderAsync(reloadedOrder, [reloadedSale], CancellationToken.None);
        await db.SaveChangesAsync();

        var stored = await db.Orders.SingleAsync();
        Assert.Equal(FulfillmentStatus.Fulfilled, stored.FulfillmentStatus); // NOT reset to Unfulfilled
        Assert.Equal(OrderStatus.Delivered, stored.Status);                  // NOT downgraded to Paid
        Assert.Equal(120m, stored.Subtotal);                                 // money did re-sync
    }

    [Fact]
    public async Task Fulfillment_never_overwrites_a_refunded_sale_line()
    {
        var (db, org, user) = NewContext(nameof(Fulfillment_never_overwrites_a_refunded_sale_line));
        await using var _ = db;
        var (order, sale) = await SeedOrderWithSale(db, org);
        sale.Status = SaleStatus.Refunded;
        await db.SaveChangesAsync();

        await NewController(db, new StubCurrentUser(org, user)).UpdateTracking(order.Id,
            new UpdateOrderTrackingRequest("USPS", "9400abc", null, null, null, false, MarkDelivered: true), CancellationToken.None);

        Assert.Equal(SaleStatus.Refunded, (await db.Sales.SingleAsync()).Status); // financial state preserved
    }
}
