using Microsoft.EntityFrameworkCore;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// What the database itself guarantees — which the in-memory provider cannot tell us, because it has no foreign keys
/// and never cascades. These run on real Postgres against the production migrations.
///
/// The rules under test are load-bearing for money: deleting an order must take its lines with it but must NOT take
/// the sales, because a sale is financial history. That is configured as Cascade on OrderLine.Order and SetNull on
/// Sale.Order — configuration nothing verified until now.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RelationalIntegrityTests(PostgresFixture postgres)
{
    private static async Task<(Order Order, Sale Sale)> SeedOrderWithSale(HaulScoutDbContext db, Guid org)
    {
        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", PurchasePrice = 40m, Status = InventoryStatus.Listed };
        db.InventoryItems.Add(item);

        Order order = new()
        {
            OrganizationId = org,
            Channel = "eBay",
            OrderNumber = $"EB-{Guid.NewGuid():N}"[..12],
            Subtotal = 120m,
            GrandTotal = 120m,
            Status = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Paid,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
        };
        db.Orders.Add(order);
        db.OrderLines.Add(new OrderLine
        {
            OrganizationId = org,
            OrderId = order.Id,
            InventoryItemId = item.Id,
            TitleSnapshot = item.Title,
            Quantity = 1,
            UnitPrice = 120m,
            LineTotal = 120m,
        });

        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            OrderId = order.Id,
            Platform = "eBay",
            SoldPrice = 120m,
            CostOfGoods = 40m,
            NetProfit = 80m,
            Status = SaleStatus.Delivered,
            SoldAt = DateTimeOffset.UtcNow,
        };
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        return (order, sale);
    }

    /// <summary>
    /// The invariant behind "delete a phantom order without destroying money": the database drops the lines and
    /// releases the sale. In-memory would happily leave orphaned lines and never tell us.
    /// </summary>
    [Fact]
    public async Task Deleting_an_order_cascades_its_lines_but_releases_its_sales()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _db = db;
        (Order order, Sale sale) = await SeedOrderWithSale(db, org);

        db.Orders.Remove(order);
        await db.SaveChangesAsync();

        // Lines belong to the order — they go with it.
        Assert.False(await db.OrderLines.AnyAsync(line => line.OrderId == order.Id));
        // The sale is financial history — it survives, merely detached.
        Sale survivor = await db.Sales.SingleAsync(item => item.Id == sale.Id);
        Assert.Null(survivor.OrderId);
    }

    /// <summary>A refund cannot point at a sale that does not exist — the database refuses, not just the C#.</summary>
    [Fact]
    public async Task A_refund_cannot_reference_a_missing_sale()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _db = db;

        db.Refunds.Add(new Refund
        {
            OrganizationId = org,
            SaleId = Guid.NewGuid(),          // no such sale
            Type = RefundType.Partial,
            ItemAmount = 10m,
            Reason = "orphan",
            RecordedByUserId = Guid.NewGuid(),
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>Deleting a sale takes its refunds with it — they have no meaning without the sale.</summary>
    [Fact]
    public async Task Deleting_a_sale_cascades_its_refunds()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _db = db;
        (_, Sale sale) = await SeedOrderWithSale(db, org);

        db.Refunds.Add(new Refund
        {
            OrganizationId = org,
            SaleId = sale.Id,
            Type = RefundType.Partial,
            ItemAmount = 10m,
            Reason = "partial",
            RecordedByUserId = user,
        });
        await db.SaveChangesAsync();

        db.Sales.Remove(sale);
        await db.SaveChangesAsync();

        Assert.False(await db.Refunds.AnyAsync(refund => refund.SaleId == sale.Id));
    }

    /// <summary>
    /// The org query filter is the tenancy boundary. Here it is enforced by a real query against real rows rather
    /// than by an in-memory dictionary that happens to be empty.
    /// </summary>
    [Fact]
    public async Task One_organizations_rows_are_invisible_to_another()
    {
        (HaulScoutDbContext dbA, Guid orgA, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _a = dbA;
        await SeedOrderWithSale(dbA, orgA);

        (HaulScoutDbContext dbB, Guid orgB, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _b = dbB;

        Assert.True(await dbA.Orders.AnyAsync());
        Assert.False(await dbB.Orders.AnyAsync());   // same table, different tenant
    }
}
