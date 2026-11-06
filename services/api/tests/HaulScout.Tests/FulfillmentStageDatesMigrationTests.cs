using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Orders damaged by the old "Mark delivered", and orders whose marketplace delivery never arrived, put through the
/// repair: a database at the step before, seeded the way production holds them, then brought up to date.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FulfillmentStageDatesMigrationTests(PostgresFixture postgres)
{
    private const string StepBefore = "20261019182951_AddFloridaSalesTax";

    private static readonly DateTimeOffset Shipped = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MarketplaceDelivered = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    // The moment of the click, with the sub-second precision only a clock has.
    private static readonly DateTimeOffset Click = new DateTimeOffset(2026, 11, 5, 15, 2, 11, TimeSpan.Zero).AddTicks(4_837_260);

    private static Order NewOrder(Guid org, string externalId, OrderStatus status, DateTimeOffset? packed, DateTimeOffset? shipped, DateTimeOffset? delivered) => new()
    {
        OrganizationId = org,
        Channel = "eBay",
        ExternalOrderId = externalId,
        OrderNumber = externalId,
        Status = status,
        PaymentStatus = PaymentStatus.Paid,
        FulfillmentStatus = shipped is null && delivered is null ? FulfillmentStatus.Unfulfilled : FulfillmentStatus.Fulfilled,
        PackedAt = packed,
        ShippedAt = shipped,
        DeliveredAt = delivered,
    };

    private static PlatformOrder Marketplace(Guid org, string externalId, DateTimeOffset? shipped, DateTimeOffset? delivered) => new()
    {
        OrganizationId = org,
        Platform = "eBay",
        ExternalOrderId = externalId,
        OrderedAt = Shipped.AddDays(-1),
        ShippedAt = shipped,
        DeliveredAt = delivered,
        FulfillmentStage = delivered is null ? PlatformFulfillmentStage.Shipped : PlatformFulfillmentStage.Delivered,
    };

    // Exactly what OrdersWorkflowService wrote before this fix.
    private static AuditLog Trail(Guid org, Order order, DateTimeOffset? shippedBefore, DateTimeOffset shippedAfter, DateTimeOffset deliveredAfter) => new()
    {
        OrganizationId = org,
        EntityName = nameof(Order),
        EntityId = order.Id,
        Action = "OrderTrackingUpdated",
        DetailsJson = JsonSerializer.Serialize(new
        {
            order.OrderNumber,
            Before = new { Status = OrderStatus.Shipped, FulfillmentStatus = FulfillmentStatus.Fulfilled, ShippingCarrier = "USPS", TrackingNumber = "9400", TrackingUrl = (string?)null, ShippedAt = shippedBefore, DeliveredAt = (DateTimeOffset?)null },
            After = new { Status = OrderStatus.Delivered, FulfillmentStatus = FulfillmentStatus.Fulfilled, ShippingCarrier = "USPS", TrackingNumber = "9400", TrackingUrl = (string?)null, ShippedAt = (DateTimeOffset?)shippedAfter, DeliveredAt = (DateTimeOffset?)deliveredAfter },
        }),
    };

    [Fact]
    public async Task Stamped_stage_dates_go_back_and_marketplace_deliveries_arrive()
    {
        Guid org = Guid.NewGuid();
        (HaulScoutDbContext db, IMigrator migrator) = await postgres.DatabaseAtAsync(StepBefore, org);
        await using HaulScoutDbContext scope = db;

        Order stamped = NewOrder(org, "19-15187-69578", OrderStatus.Delivered, Click, Click, Click);
        DateTimeOffset typed = new(2026, 10, 2, 14, 30, 0, TimeSpan.Zero);
        Order typedByHand = NewOrder(org, "19-00000-00001", OrderStatus.Delivered, null, typed, typed);
        Order correctedSince = NewOrder(org, "19-00000-00002", OrderStatus.Delivered, null, Click, Click.AddDays(-1));
        Order neverShippedHere = NewOrder(org, "19-00000-00003", OrderStatus.Delivered, Click, Click, Click);
        Order waiting = NewOrder(org, "19-00000-00004", OrderStatus.Shipped, null, Shipped, null);
        Order cancelled = NewOrder(org, "19-00000-00005", OrderStatus.Cancelled, null, null, null);
        db.Orders.AddRange(stamped, typedByHand, correctedSince, neverShippedHere, waiting, cancelled);
        db.PlatformOrders.AddRange(
            Marketplace(org, stamped.ExternalOrderId!, Shipped, MarketplaceDelivered),
            Marketplace(org, waiting.ExternalOrderId!, Shipped, MarketplaceDelivered),
            Marketplace(org, cancelled.ExternalOrderId!, Shipped, MarketplaceDelivered));
        db.AuditLogs.AddRange(
            Trail(org, stamped, Shipped, Click, Click),
            Trail(org, typedByHand, Shipped, typed, typed),
            Trail(org, correctedSince, Shipped, Click, Click),
            Trail(org, neverShippedHere, null, Click, Click));
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();

        db.ChangeTracker.Clear();
        Dictionary<Guid, Order> orders = await db.Orders.ToDictionaryAsync(order => order.Id);

        // The order from the report: shipped back to Sep 24, packed unrecorded, delivered on the marketplace's day.
        Assert.Null(orders[stamped.Id].PackedAt);
        Assert.Equal(Shipped, orders[stamped.Id].ShippedAt);
        Assert.Equal(MarketplaceDelivered, orders[stamped.Id].DeliveredAt);

        // Dates a person typed, and dates corrected after the click, are theirs.
        Assert.Equal(typed, orders[typedByHand.Id].ShippedAt);
        Assert.Equal(typed, orders[typedByHand.Id].DeliveredAt);
        Assert.Equal(Click.AddDays(-1), orders[correctedSince.Id].DeliveredAt);

        // Nothing recorded a shipment before the click, and nothing else knows one: shipped and packed are unknown.
        Assert.Null(orders[neverShippedHere.Id].ShippedAt);
        Assert.Null(orders[neverShippedHere.Id].PackedAt);
        Assert.Equal(Click, orders[neverShippedHere.Id].DeliveredAt);

        // A delivery the marketplace reported reaches the order still waiting for it; a cancelled order stays closed.
        Assert.Equal(MarketplaceDelivered, orders[waiting.Id].DeliveredAt);
        Assert.Equal(OrderStatus.Delivered, orders[waiting.Id].Status);
        Assert.Null(orders[cancelled.Id].DeliveredAt);
        Assert.Equal(OrderStatus.Cancelled, orders[cancelled.Id].Status);

        List<AuditLog> repairs = await db.AuditLogs
            .Where(log => log.Action == "OrderStageDatesRepaired" || log.Action == "OrderDeliveryFromMarketplace")
            .ToListAsync();
        Assert.Equal(
            new HashSet<(Guid, string)> { (stamped.Id, "OrderStageDatesRepaired"), (neverShippedHere.Id, "OrderStageDatesRepaired"), (waiting.Id, "OrderDeliveryFromMarketplace") },
            repairs.Select(log => (log.EntityId!.Value, log.Action)).ToHashSet());
        Assert.Contains("\"PackedAt\"", repairs.First(log => log.EntityId == stamped.Id).DetailsJson);
    }
}
