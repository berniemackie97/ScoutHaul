using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Services;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class SaleAccountingTests
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

    private static Sale SeedSale(HaulScoutDbContext db, Guid org)
    {
        InventoryItem item = new() { Title = "Brass Lamp", PurchasePrice = 40m };
        db.InventoryItems.Add(item);
        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 120m,
            ShippingCharged = 0m,
            CostOfGoods = 40m,
            PlatformFees = 15.60m,
            PaymentFees = 3.80m,
            ShippingCost = 8m,
            SuppliesCost = 0m,
            OtherCosts = 0m,
            SalesTaxCollected = 9.90m,
            TaxRemittedByPlatform = true,
            SoldAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
        sale.RecomputeNet();
        db.Sales.Add(sale);
        db.SaveChanges();
        return sale;
    }

    [Theory]
    [InlineData(9.90, true, 0)]      // marketplace facilitator remits → pass-through, we owe nothing
    [InlineData(9.90, false, 9.90)]  // we collected it → we owe it (liability)
    public void SalesTaxOwed_depends_on_who_remits(decimal collected, bool remitted, decimal expected)
    {
        Assert.Equal(expected, ResaleFinancialCalculator.SalesTaxOwed(collected, remitted));
    }

    [Fact]
    public void Net_profit_excludes_tax_entirely()
    {
        RealizedSaleFinancials f = ResaleFinancialCalculator.CalculateRealizedSale(120m, 0m, 40m, 15.60m, 3.80m, 8m, 0m, 0m);
        Assert.Equal(52.60m, f.NetProfit);
    }

    [Fact]
    public async Task Editing_a_sale_recomputes_net_backdates_and_writes_a_correction()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Editing_a_sale_recomputes_net_backdates_and_writes_a_correction));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        DateTimeOffset backdated = DateTimeOffset.UtcNow.AddDays(-30);
        ActionResult<SaleDto> result = await controller.Update(sale.Id, new UpdateSaleRequest(
            Platform: "eBay", SoldPrice: 120m, ShippingCharged: 0m, PlatformFees: 20m, PaymentFees: 3.80m,
            ShippingCost: 8m, SuppliesCost: 0m, OtherCosts: 0m, SalesTaxCollected: 9.90m,
            TaxRemittedByPlatform: false, SoldAt: backdated, Notes: null,
            Reason: "Actual eBay fee was higher than estimated"), CancellationToken.None);

        Assert.Null(result.Result);
        SaleDto dto = result.Value!;
        Assert.Equal(48.20m, dto.NetProfit);   // 120 - 40 - 20 - 3.80 - 8 (tax excluded)
        Assert.Equal(9.90m, dto.SalesTaxOwed);  // now seller-owed
        Assert.Equal(backdated, dto.SoldAt);

        SaleCorrection correction = Assert.Single(await db.SaleCorrections.Where(c => c.SaleId == sale.Id).ToListAsync());
        Assert.Equal(1, correction.Version);
        Assert.Equal(user, correction.CorrectedByUserId);
        Assert.Contains("higher than estimated", correction.Reason);
        Assert.Contains("52.60", correction.BeforeJson); // prior net preserved
        Assert.Contains("48.20", correction.AfterJson);  // new net captured
    }

    [Fact]
    public async Task A_second_edit_increments_the_correction_version()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_second_edit_increments_the_correction_version));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));
        UpdateSaleRequest Req(string reason) => new("eBay", 120m, 0m, 16m, 3.80m, 8m, 0m, 0m, 9.90m, true, sale.SoldAt, null, reason);

        await controller.Update(sale.Id, Req("first fix"), CancellationToken.None);
        await controller.Update(sale.Id, Req("second fix"), CancellationToken.None);

        List<int> versions = await db.SaleCorrections
            .Where(c => c.SaleId == sale.Id).Select(c => c.Version).OrderBy(v => v).ToListAsync();
        Assert.Equal([1, 2], versions);
    }

    [Fact]
    public async Task Editing_requires_a_reason()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Editing_requires_a_reason));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        ActionResult<SaleDto> result = await controller.Update(sale.Id, new UpdateSaleRequest(
            "eBay", 120m, 0m, 16m, 3.80m, 8m, 0m, 0m, 9.90m, true, sale.SoldAt, null, "  "), CancellationToken.None);

        ProblemAssert.Invalid(result.Result);
        Assert.Empty(await db.SaleCorrections.ToListAsync());
    }

    [Fact]
    public async Task Recording_rejects_a_future_sale_without_mutating_inventory()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Recording_rejects_a_future_sale_without_mutating_inventory));
        await using HaulScoutDbContext _ = db;
        InventoryItem item = new()
        {
            OrganizationId = org,
            Title = "Walnut Desk",
            Status = InventoryStatus.Listed,
            PurchasePrice = 75m,
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        ActionResult<SaleDto> result = await controller.Record(new RecordSaleRequest(
            item.Id, null, "eBay", 240m, 31m, 7m, 20m, 2m, 0m,
            DateTimeOffset.UtcNow.AddDays(1), null), CancellationToken.None);

        ProblemAssert.Invalid(result.Result);
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Equal(InventoryStatus.Listed, item.Status);
        Assert.Null(item.SoldAt);
    }

    [Fact]
    public async Task Fulfillment_transitions_are_ordered_audited_and_create_one_shipment()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Fulfillment_transitions_are_ordered_audited_and_create_one_shipment));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        sale.InventoryItem!.Status = InventoryStatus.Sold;
        await db.SaveChangesAsync();
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Packed, sale.SoldAt.AddDays(1), null, null, null), CancellationToken.None);
        await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Shipped, sale.SoldAt.AddDays(2), null, "USPS", "9400TEST"), CancellationToken.None);
        ActionResult<SaleDto> delivered = await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Delivered, sale.SoldAt.AddDays(5), null, null, null), CancellationToken.None);

        Assert.Equal(SaleStatus.Delivered, delivered.Value!.Status);
        Assert.Equal(InventoryStatus.Delivered, sale.InventoryItem.Status);
        Shipment shipment = Assert.Single(await db.Shipments.Where(item => item.SaleId == sale.Id).ToListAsync());
        Assert.Equal("USPS", shipment.Carrier);
        Assert.NotNull(shipment.PackedAt);
        Assert.NotNull(shipment.ShippedAt);
        Assert.NotNull(shipment.DeliveredAt);
        Assert.Equal(3, await db.SaleCorrections.CountAsync(item => item.SaleId == sale.Id));
    }

    [Fact]
    public async Task Fulfillment_rejects_skipped_stages_and_requires_exception_reasons()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Fulfillment_rejects_skipped_stages_and_requires_exception_reasons));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        ActionResult<SaleDto> skipped = await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Delivered, null, null, null, null), CancellationToken.None);
        await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Packed, sale.SoldAt.AddDays(2), null, null, null), CancellationToken.None);
        ActionResult<SaleDto> chronologicallyInvalid = await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Shipped, sale.SoldAt.AddDays(1), null, null, null), CancellationToken.None);
        ActionResult<SaleDto> cancelledWithoutReason = await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Cancelled, null, null, null, null), CancellationToken.None);

        ProblemAssert.Blocked(skipped.Result);
        ProblemAssert.Invalid(chronologicallyInvalid.Result);
        ProblemAssert.Invalid(cancelledWithoutReason.Result);
    }

    [Fact]
    public async Task Return_and_refund_release_inventory_for_a_future_resale()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Return_and_refund_release_inventory_for_a_future_resale));
        await using HaulScoutDbContext _ = db;
        Sale sale = SeedSale(db, org);
        sale.Status = SaleStatus.Delivered;
        sale.InventoryItem!.Status = InventoryStatus.Delivered;
        await db.SaveChangesAsync();
        SalesController controller = SalesControllerFactory.Create(db, new StubCurrentUser(org, user));

        await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.ReturnRequested, null, "Customer reported damage", null, null), CancellationToken.None);
        await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Returned, null, "Return received and inspected", null, null), CancellationToken.None);
        ActionResult<SaleDto> refunded = await controller.Transition(sale.Id, new SaleStatusTransitionRequest(SaleStatus.Refunded, null, "Full refund issued", null, null), CancellationToken.None);

        Assert.Equal(SaleStatus.Refunded, refunded.Value!.Status);
        Assert.Equal(InventoryStatus.Returned, sale.InventoryItem.Status);
        Assert.True(SaleWorkflow.ReleasesInventory(sale.Status));
        Assert.False(SaleWorkflow.IsRealized(sale.Status));
    }
}
