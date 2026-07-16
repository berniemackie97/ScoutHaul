using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Domain.Services;

namespace HaulScout.Tests;

public class RefundTests
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
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, org, user);
    }

    // Sold 100 + 10 ship, COGS 30, fees 15, label 8 → net 57.
    private static Sale SeedSale(HaulScoutDbContext db, Guid org)
    {
        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", PurchasePrice = 30m, Status = InventoryStatus.Delivered };
        db.InventoryItems.Add(item);
        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 100m,
            ShippingCharged = 10m,
            CostOfGoods = 30m,
            PlatformFees = 15m,
            ShippingCost = 8m,
            Status = SaleStatus.Delivered,
            InventoryItem = item,
        };
        sale.RecomputeNet();
        db.Sales.Add(sale);
        db.SaveChanges();
        return sale;
    }

    // --- Pure calculator ---

    [Fact]
    public void Partial_refund_reduces_net_and_fee_credit_lifts_it_back()
    {
        // item 100→60 after $40 refund, fees 15→10 after $5 credit: 60 + 10 ship − 30 cogs − 10 fee − 8 label = 22.
        ResaleFinancialCalculator.SaleRefundImpact impact = new(40m, 0m, 0m, 5m, ItemRestocked: false);
        decimal net = ResaleFinancialCalculator.NetProfitAfterRefunds(100m, 10m, 30m, 15m, 0m, 8m, 0m, 0m, impact);
        Assert.Equal(22m, net);
    }

    [Fact]
    public void Full_refund_with_return_drops_cogs_and_keeps_sunk_shipping_cost()
    {
        // item 0, ship 0, all fees credited, item back so COGS 0, but the $8 label is already spent → −8.
        ResaleFinancialCalculator.SaleRefundImpact impact = new(100m, 10m, 0m, 15m, ItemRestocked: true);
        decimal net = ResaleFinancialCalculator.NetProfitAfterRefunds(100m, 10m, 30m, 15m, 0m, 8m, 0m, 0m, impact);
        Assert.Equal(-8m, net);
    }

    // --- Endpoint behavior ---

    [Fact]
    public async Task Recording_a_partial_refund_recomputes_net_keeps_the_item_sold_and_audits()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Recording_a_partial_refund_recomputes_net_keeps_the_item_sold_and_audits));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        ActionResult<SaleDto> result = await controller.RecordRefund(sale.Id,
            new RecordRefundRequest(RefundType.Partial, ItemAmount: 40m, Reason: "Buyer kept it, partial refund", FeeCredit: 5m),
            CancellationToken.None);

        SaleDto dto = result.Value!;
        Assert.Equal(SaleStatus.PartiallyRefunded, dto.Status);
        Assert.Equal(22m, dto.NetProfit);
        Assert.Equal(40m, dto.TotalRefunded);
        Assert.Equal(1, dto.RefundCount);

        InventoryItem item = await db.InventoryItems.SingleAsync(i => i.Id == sale.InventoryItemId);
        Assert.Equal(InventoryStatus.Delivered, item.Status); // still with the buyer, not released
        Assert.Single(await db.SaleCorrections.Where(c => c.SaleId == sale.Id).ToListAsync());
        Assert.Single(await db.Refunds.Where(r => r.SaleId == sale.Id).ToListAsync());
    }

    [Fact]
    public async Task A_full_refund_with_return_releases_the_item_and_sets_returned()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_full_refund_with_return_releases_the_item_and_sets_returned));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        ActionResult<SaleDto> result = await controller.RecordRefund(sale.Id,
            new RecordRefundRequest(RefundType.Full, ItemAmount: 100m, Reason: "Returned, full refund",
                ShippingRefunded: 10m, FeeCredit: 15m, ItemReturned: true),
            CancellationToken.None);

        SaleDto dto = result.Value!;
        Assert.Equal(SaleStatus.Returned, dto.Status);
        Assert.Equal(-8m, dto.NetProfit); // sunk shipping label only
        InventoryItem item = await db.InventoryItems.SingleAsync(i => i.Id == sale.InventoryItemId);
        Assert.Equal(InventoryStatus.Returned, item.Status); // back in inventory to resell
    }

    [Fact]
    public async Task Voiding_the_last_refund_restores_net_and_reverts_status()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Voiding_the_last_refund_restores_net_and_reverts_status));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        await controller.RecordRefund(sale.Id, new RecordRefundRequest(RefundType.Partial, 40m, "mistake", FeeCredit: 5m), CancellationToken.None);
        Refund refund = await db.Refunds.SingleAsync(r => r.SaleId == sale.Id);

        ActionResult<SaleDto> result = await controller.VoidRefund(sale.Id, refund.Id, CancellationToken.None);

        SaleDto dto = result.Value!;
        Assert.Equal(57m, dto.NetProfit);            // original net restored
        Assert.Equal(SaleStatus.Delivered, dto.Status);
        Assert.Equal(0, dto.RefundCount);
        Assert.Empty(await db.Refunds.Where(r => r.SaleId == sale.Id).ToListAsync());
        Assert.Equal(2, await db.SaleCorrections.CountAsync(c => c.SaleId == sale.Id)); // record + void both audited
    }

    [Fact]
    public async Task A_refund_exceeding_the_sold_price_is_rejected()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_refund_exceeding_the_sold_price_is_rejected));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        ActionResult<SaleDto> result = await controller.RecordRefund(sale.Id,
            new RecordRefundRequest(RefundType.Partial, ItemAmount: 150m, Reason: "too much"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task A_refund_needs_a_reason_and_a_non_zero_amount()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_refund_needs_a_reason_and_a_non_zero_amount));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        Assert.IsType<BadRequestObjectResult>((await controller.RecordRefund(sale.Id,
            new RecordRefundRequest(RefundType.Partial, 10m, "  "), CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.RecordRefund(sale.Id,
            new RecordRefundRequest(RefundType.Partial, 0m, "nothing refunded"), CancellationToken.None)).Result);
    }
}
