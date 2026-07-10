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
        return new PlatformOrdersController(db, currentUser, new OrderAssembler(db, currentUser));
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
}
