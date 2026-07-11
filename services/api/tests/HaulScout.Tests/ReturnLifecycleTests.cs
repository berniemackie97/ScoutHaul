using Microsoft.AspNetCore.Mvc;
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
/// Returns are a lifecycle, not a single money event: the seller pays return shipping up front, the refund is issued
/// only once the item is back and inspected, and a resellable item restocks. These pin that flow.
/// </summary>
public class ReturnLifecycleTests
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

    private static SalesController NewSales(HaulScoutDbContext db, Guid org, Guid user) =>
        new(db, new StubCurrentUser(org, user), new OrderAssembler(db, new StubCurrentUser(org, user)));

    private static async Task<Guid> SeedSale(HaulScoutDbContext db, Guid org, Guid user, SalesController sales)
    {
        var item = new InventoryItem { OrganizationId = org, Title = "Brass Lamp", PurchasePrice = 10m, Status = InventoryStatus.Listed };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        // SoldPrice 100, platform 10, payment 3, seller shipping 5, COGS 10 → net 72.
        ActionResult<SaleDto> recorded = await sales.Record(new RecordSaleRequest(
            item.Id, null, "eBay", 100m, 10m, 3m, 5m, 0m, 0m,
            DateTimeOffset.UtcNow.AddDays(-3), null, ExternalOrderId: "EB-RMA"), CancellationToken.None);
        SaleDto created = Assert.IsType<CreatedAtActionResult>(recorded.Result).Value as SaleDto
            ?? throw new InvalidOperationException("Expected sale DTO.");
        return created.Id;
    }

    [Fact]
    public async Task Full_return_books_shipping_then_refunds_on_receipt_and_restocks()
    {
        var (db, org, user) = NewContext(nameof(Full_return_books_shipping_then_refunds_on_receipt_and_restocks));
        await using var _ = db;
        SalesController sales = NewSales(db, org, user);
        Guid saleId = await SeedSale(db, org, user, sales);

        decimal netAtSale = (await db.Sales.AsNoTracking().SingleAsync(sale => sale.Id == saleId)).NetProfit;

        ReturnDto rma = (await sales.OpenReturn(saleId, new OpenReturnRequest("Doesn't fit"), CancellationToken.None)).Value!;
        Assert.Equal(ReturnStatus.Requested, rma.Status);

        // Issue the label → return shipping booked as a cost, net drops by it.
        ReturnDto labeled = (await sales.IssueReturnLabel(rma.Id, new IssueReturnLabelRequest(8m), CancellationToken.None)).Value!;
        Assert.Equal(ReturnStatus.LabelIssued, labeled.Status);
        Assert.Equal(8m, labeled.ReturnShippingCost);
        Assert.Equal(netAtSale - 8m, (await db.Sales.AsNoTracking().SingleAsync(sale => sale.Id == saleId)).NetProfit);

        // A refund cannot be issued before the item is received back.
        ActionResult<ReturnDto> early = await sales.ResolveReturn(rma.Id, new ResolveReturnRequest(100m), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(early.Result);

        Assert.Equal(ReturnStatus.Received, (await sales.ReceiveReturn(rma.Id, CancellationToken.None)).Value!.Status);

        ReturnDto resolved = (await sales.ResolveReturn(rma.Id,
            new ResolveReturnRequest(100m, RefundType.Full, Restock: true), CancellationToken.None)).Value!;
        Assert.Equal(ReturnStatus.Refunded, resolved.Status);
        Assert.True(resolved.Restocked);
        Assert.NotNull(resolved.RefundId);

        Sale after = await db.Sales.Include(sale => sale.Refunds).Include(sale => sale.InventoryItem)
            .AsNoTracking().SingleAsync(sale => sale.Id == saleId);
        Assert.Single(after.Refunds);
        Assert.Equal(SaleStatus.Returned, after.Status);                       // restocked → Returned
        Assert.Equal(InventoryStatus.Returned, after.InventoryItem!.Status);   // item back in inventory
    }

    [Fact]
    public async Task Rejecting_a_received_return_keeps_the_booked_return_shipping_and_issues_no_refund()
    {
        var (db, org, user) = NewContext(nameof(Rejecting_a_received_return_keeps_the_booked_return_shipping_and_issues_no_refund));
        await using var _ = db;
        SalesController sales = NewSales(db, org, user);
        Guid saleId = await SeedSale(db, org, user, sales);

        ReturnDto rma = (await sales.OpenReturn(saleId, new OpenReturnRequest("Item not received as described"), CancellationToken.None)).Value!;
        await sales.IssueReturnLabel(rma.Id, new IssueReturnLabelRequest(8m), CancellationToken.None);
        await sales.ReceiveReturn(rma.Id, CancellationToken.None);

        ReturnDto rejected = (await sales.RejectReturn(rma.Id, new RejectReturnRequest("Wrong item shipped back"), CancellationToken.None)).Value!;
        Assert.Equal(ReturnStatus.Rejected, rejected.Status);

        Sale after = await db.Sales.Include(sale => sale.Refunds).AsNoTracking().SingleAsync(sale => sale.Id == saleId);
        Assert.Empty(after.Refunds);                       // no money refunded
        Assert.Equal(64m, after.NetProfit);                // 72 − 8 return shipping still stands (we paid the label)
    }

    [Fact]
    public async Task A_sale_can_only_have_one_open_return()
    {
        var (db, org, user) = NewContext(nameof(A_sale_can_only_have_one_open_return));
        await using var _ = db;
        SalesController sales = NewSales(db, org, user);
        Guid saleId = await SeedSale(db, org, user, sales);

        await sales.OpenReturn(saleId, new OpenReturnRequest("First"), CancellationToken.None);
        ActionResult<ReturnDto> second = await sales.OpenReturn(saleId, new OpenReturnRequest("Second"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(second.Result);
    }
}
