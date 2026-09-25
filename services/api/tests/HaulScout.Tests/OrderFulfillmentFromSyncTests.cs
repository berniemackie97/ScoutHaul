using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Getting an order to the buyer is the part of it that keeps changing after the sale: a label is bought, a carrier
/// scans it, it arrives. A scan that reads an order a second time has to be able to say so.
/// </summary>
public sealed class OrderFulfillmentFromSyncTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, OrderAssembler Assembler) NewHarness(string name)
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(name).Options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, new OrderAssembler(db, new StubCurrentUser(org)));
    }

    private static Task<Order> Sync(
        OrderAssembler assembler,
        string? carrier = null,
        string? tracking = null,
        DateTimeOffset? shippedAt = null,
        DateTimeOffset? deliveredAt = null) =>
        assembler.GetOrCreateAsync(
            "eBay",
            "26-15152-72360",
            null,
            "buyer99",
            "A Buyer",
            DateTimeOffset.UtcNow.AddDays(-3),
            null,
            20m,
            6.99m,
            0m,
            CancellationToken.None,
            shippingCarrier: carrier,
            trackingNumber: tracking,
            shippedAt: shippedAt,
            deliveredAt: deliveredAt);

    [Fact]
    public async Task A_later_scan_brings_the_tracking_the_first_one_could_not_know()
    {
        // The sale is read minutes after it happens. The label is bought hours later.
        var (db, assembler) = NewHarness(nameof(A_later_scan_brings_the_tracking_the_first_one_could_not_know));
        await using HaulScoutDbContext _ = db;
        Order created = await Sync(assembler);
        await db.SaveChangesAsync();

        Assert.Null(created.TrackingNumber);
        Assert.Equal(FulfillmentStatus.Unfulfilled, created.FulfillmentStatus);

        DateTimeOffset shipped = DateTimeOffset.UtcNow.AddDays(-1);
        Order updated = await Sync(assembler, carrier: "USPS", tracking: "9400100000000000000000", shippedAt: shipped);
        await db.SaveChangesAsync();

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("9400100000000000000000", updated.TrackingNumber);
        Assert.Equal("USPS", updated.ShippingCarrier);
        Assert.Equal(shipped, updated.ShippedAt);
        Assert.Equal(FulfillmentStatus.Fulfilled, updated.FulfillmentStatus);
        Assert.Single(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Delivery_reaches_the_order_it_belongs_to()
    {
        var (db, assembler) = NewHarness(nameof(Delivery_reaches_the_order_it_belongs_to));
        await using HaulScoutDbContext _ = db;
        await Sync(assembler, carrier: "USPS", tracking: "9400100000000000000000", shippedAt: DateTimeOffset.UtcNow.AddDays(-2));
        await db.SaveChangesAsync();

        DateTimeOffset delivered = DateTimeOffset.UtcNow.AddHours(-5);
        Order updated = await Sync(assembler, deliveredAt: delivered);
        await db.SaveChangesAsync();

        Assert.Equal(delivered, updated.DeliveredAt);
        // And what the earlier scan knew is still there: this scan simply did not mention it.
        Assert.Equal("9400100000000000000000", updated.TrackingNumber);
        Assert.Equal("USPS", updated.ShippingCarrier);
        Assert.NotNull(updated.ShippedAt);
    }

    [Fact]
    public async Task A_scan_that_knows_nothing_erases_nothing()
    {
        var (db, assembler) = NewHarness(nameof(A_scan_that_knows_nothing_erases_nothing));
        await using HaulScoutDbContext _ = db;
        await Sync(assembler, carrier: "USPS", tracking: "9400100000000000000000", shippedAt: DateTimeOffset.UtcNow.AddDays(-2), deliveredAt: DateTimeOffset.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();

        Order updated = await Sync(assembler);
        await db.SaveChangesAsync();

        Assert.Equal("9400100000000000000000", updated.TrackingNumber);
        Assert.NotNull(updated.DeliveredAt);
        Assert.Equal(FulfillmentStatus.Fulfilled, updated.FulfillmentStatus);
    }

    [Fact]
    public async Task A_corrected_tracking_number_replaces_the_one_that_was_wrong()
    {
        // The seller re-shipped. The marketplace's newer number is the true one; the order must not keep the old.
        var (db, assembler) = NewHarness(nameof(A_corrected_tracking_number_replaces_the_one_that_was_wrong));
        await using HaulScoutDbContext _ = db;
        await Sync(assembler, carrier: "USPS", tracking: "9400100000000000000000");
        await db.SaveChangesAsync();

        Order updated = await Sync(assembler, carrier: "UPS", tracking: "1Z999AA10123456784");
        await db.SaveChangesAsync();

        Assert.Equal("1Z999AA10123456784", updated.TrackingNumber);
        Assert.Equal("UPS", updated.ShippingCarrier);
    }
}
