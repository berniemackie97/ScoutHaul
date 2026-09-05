using Microsoft.EntityFrameworkCore;
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
/// The orders screen counted a window and labelled it the business.
///
/// It fetched the newest 200 orders, bucketed them by stage in the browser, and rendered
/// "Captured revenue · Paid orders" from that slice. On the two-hundred-and-first order every figure
/// on the page quietly became wrong, and every stage tab became a view of the recent past rather than
/// of the work waiting.
///
/// The stage is derived from the order's own facts, so it cannot be a WHERE clause without restating
/// the rule in SQL. These pin that the counts span the whole book anyway, and that the rule computing
/// them is still <see cref="OrderWorkflow"/> and not a second copy.
/// </summary>
public sealed class OrderQueueTruthTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (OrdersWorkflowService Orders, HaulScoutDbContext Db, Guid Org) NewService()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();

        StubCurrentUser currentUser = new(org);
        return (new OrdersWorkflowService(db, currentUser, new OrderAssembler(db, currentUser)), db, org);
    }

    private static Order Order(
        Guid org,
        string number,
        PaymentStatus payment = PaymentStatus.Paid,
        decimal grandTotal = 100m,
        bool packed = false,
        bool localPickup = false) =>
        new()
        {
            OrganizationId = org,
            OrderNumber = number,
            Status = OrderStatus.Paid,
            PaymentStatus = payment,
            GrandTotal = grandTotal,
            PackedAt = packed ? DateTimeOffset.UtcNow : null,
            IsLocalPickup = localPickup,
        };

    private static Task<OrderListDto> Browse(OrdersWorkflowService orders, string? stage = null, int pageSize = 25) =>
        orders.BrowseAsync(stage, search: null, channel: null, page: 1, pageSize, CancellationToken.None);

    [Fact]
    public async Task Captured_revenue_is_every_paid_order_not_the_newest_page_of_them()
    {
        (OrdersWorkflowService orders, HaulScoutDbContext db, Guid org) = NewService();
        for (int index = 0; index < 250; index++)
        {
            db.Orders.Add(Order(org, $"SH-{index:0000}", grandTotal: 10m));
        }
        await db.SaveChangesAsync();

        OrderListDto result = await Browse(orders, pageSize: 25);

        Assert.Equal(25, result.Items.Count);
        Assert.Equal(250, result.Summary.TotalOrders);
        Assert.Equal(2500m, result.Summary.CapturedRevenue);
    }

    [Fact]
    public async Task Unpaid_orders_are_not_revenue()
    {
        (OrdersWorkflowService orders, HaulScoutDbContext db, Guid org) = NewService();
        db.Orders.AddRange(
            Order(org, "SH-0001", grandTotal: 100m),
            Order(org, "SH-0002", payment: PaymentStatus.Pending, grandTotal: 500m));
        await db.SaveChangesAsync();

        OrderListDto result = await Browse(orders);

        Assert.Equal(100m, result.Summary.CapturedRevenue);
        Assert.Equal(2, result.Summary.TotalOrders);
    }

    [Fact]
    public async Task Stage_counts_span_the_whole_book()
    {
        (OrdersWorkflowService orders, HaulScoutDbContext db, Guid org) = NewService();
        for (int index = 0; index < 30; index++)
        {
            db.Orders.Add(Order(org, $"PACK-{index:000}"));
        }
        for (int index = 0; index < 12; index++)
        {
            db.Orders.Add(Order(org, $"PICK-{index:000}", packed: true, localPickup: true));
        }
        await db.SaveChangesAsync();

        OrderListDto result = await Browse(orders, pageSize: 5);

        Assert.Equal(5, result.Items.Count);
        Assert.Equal(30, StageCount(result, OrderWorkflowStage.ToPack));
        Assert.Equal(12, StageCount(result, OrderWorkflowStage.ReadyForPickup));
        Assert.Equal(42, result.Summary.OpenWork);
    }

    [Fact]
    public async Task Every_stage_is_reported_even_the_empty_ones()
    {
        // A queue that reads zero is information. A queue that is simply missing makes the caller
        // guess whether it is empty or whether the server forgot about it.
        (OrdersWorkflowService orders, HaulScoutDbContext db, Guid org) = NewService();
        db.Orders.Add(Order(org, "SH-0001"));
        await db.SaveChangesAsync();

        OrderListDto result = await Browse(orders);

        Assert.Equal(Enum.GetValues<OrderWorkflowStage>().Length, result.Summary.Stages.Count);
        Assert.Equal(0, StageCount(result, OrderWorkflowStage.Delivered));
    }

    [Fact]
    public async Task Filtering_to_a_stage_pages_that_stage_and_still_reports_the_rest()
    {
        (OrdersWorkflowService orders, HaulScoutDbContext db, Guid org) = NewService();
        for (int index = 0; index < 8; index++)
        {
            db.Orders.Add(Order(org, $"PACK-{index:000}"));
        }
        for (int index = 0; index < 3; index++)
        {
            db.Orders.Add(Order(org, $"PICK-{index:000}", packed: true, localPickup: true));
        }
        await db.SaveChangesAsync();

        OrderListDto result = await Browse(orders, stage: "ReadyForPickup");

        Assert.Equal(3, result.Total);
        Assert.All(result.Items, order => Assert.StartsWith("PICK-", order.OrderNumber));
        // Narrowing the view must not narrow the figures beside it.
        Assert.Equal(11, result.Summary.TotalOrders);
        Assert.Equal(8, StageCount(result, OrderWorkflowStage.ToPack));
    }

    [Fact]
    public async Task An_order_with_a_return_in_flight_needs_attention()
    {
        // Proves the stage still comes from OrderWorkflow rather than from a second rule written in
        // SQL: the open return is not a column on the order.
        (OrdersWorkflowService orders, HaulScoutDbContext db, Guid org) = NewService();
        Order order = Order(org, "SH-0001");
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", Status = InventoryStatus.Sold };
        db.InventoryItems.Add(item);
        Sale sale = new() { OrganizationId = org, InventoryItemId = item.Id, OrderId = order.Id, Platform = "eBay", SoldPrice = 100m };
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        db.Returns.Add(Return.Open(org, sale, "damage", null, Guid.NewGuid(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        OrderListDto result = await Browse(orders);

        Assert.Equal(1, result.Summary.NeedsAttention);
        Assert.Equal(1, StageCount(result, OrderWorkflowStage.NeedsAttention));
        Assert.Equal(0, StageCount(result, OrderWorkflowStage.ToPack));
    }

    [Fact]
    public async Task An_unknown_stage_shows_everything_rather_than_nothing()
    {
        // A stale bookmark or a renamed stage must not render an empty screen that looks like an
        // empty business.
        (OrdersWorkflowService orders, HaulScoutDbContext db, Guid org) = NewService();
        db.Orders.AddRange(Order(org, "SH-0001"), Order(org, "SH-0002"));
        await db.SaveChangesAsync();

        OrderListDto result = await Browse(orders, stage: "NotAStage");

        Assert.Equal(2, result.Total);
    }

    private static int StageCount(OrderListDto result, OrderWorkflowStage stage) =>
        result.Summary.Stages.Single(entry => entry.Stage == stage.ToString()).Count;
}
