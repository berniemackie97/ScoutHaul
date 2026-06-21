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

    private static Sale SeedSale(HaulScoutDbContext db, Guid org)
    {
        var item = new InventoryItem { Title = "Brass Lamp", PurchasePrice = 40m };
        db.InventoryItems.Add(item);
        var sale = new Sale
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 120m,
            CostOfGoods = 40m,
            PlatformFees = 15.60m,
            PaymentFees = 3.80m,
            ShippingCost = 8m,
            SuppliesCost = 0m,
            OtherCosts = 0m,
            SalesTaxCollected = 9.90m,
            TaxRemittedByPlatform = true,
            NetProfit = 52.60m,
            SoldAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
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
        var f = ResaleFinancialCalculator.CalculateRealizedSale(120m, 40m, 15.60m, 3.80m, 8m, 0m, 0m);
        Assert.Equal(52.60m, f.NetProfit);
    }

    [Fact]
    public async Task Editing_a_sale_recomputes_net_backdates_and_writes_a_correction()
    {
        var (db, org, user) = NewContext(nameof(Editing_a_sale_recomputes_net_backdates_and_writes_a_correction));
        await using var _ = db;
        var sale = SeedSale(db, org);
        var controller = new SalesController(db, new StubCurrentUser(org, user));

        var backdated = DateTimeOffset.UtcNow.AddDays(-30);
        var result = await controller.Update(sale.Id, new UpdateSaleRequest(
            Platform: "eBay", SoldPrice: 120m, PlatformFees: 20m, PaymentFees: 3.80m,
            ShippingCost: 8m, SuppliesCost: 0m, OtherCosts: 0m, SalesTaxCollected: 9.90m,
            TaxRemittedByPlatform: false, SoldAt: backdated, Notes: null,
            Reason: "Actual eBay fee was higher than estimated"), CancellationToken.None);

        Assert.Null(result.Result);
        var dto = result.Value!;
        Assert.Equal(48.20m, dto.NetProfit);   // 120 - 40 - 20 - 3.80 - 8 (tax excluded)
        Assert.Equal(9.90m, dto.SalesTaxOwed);  // now seller-owed
        Assert.Equal(backdated, dto.SoldAt);

        var correction = Assert.Single(await db.SaleCorrections.Where(c => c.SaleId == sale.Id).ToListAsync());
        Assert.Equal(1, correction.Version);
        Assert.Equal(user, correction.CorrectedByUserId);
        Assert.Contains("higher than estimated", correction.Reason);
        Assert.Contains("52.60", correction.BeforeJson); // prior net preserved
        Assert.Contains("48.20", correction.AfterJson);  // new net captured
    }

    [Fact]
    public async Task A_second_edit_increments_the_correction_version()
    {
        var (db, org, user) = NewContext(nameof(A_second_edit_increments_the_correction_version));
        await using var _ = db;
        var sale = SeedSale(db, org);
        var controller = new SalesController(db, new StubCurrentUser(org, user));
        UpdateSaleRequest Req(string reason) => new("eBay", 120m, 16m, 3.80m, 8m, 0m, 0m, 9.90m, true, sale.SoldAt, null, reason);

        await controller.Update(sale.Id, Req("first fix"), CancellationToken.None);
        await controller.Update(sale.Id, Req("second fix"), CancellationToken.None);

        var versions = await db.SaleCorrections
            .Where(c => c.SaleId == sale.Id).Select(c => c.Version).OrderBy(v => v).ToListAsync();
        Assert.Equal([1, 2], versions);
    }

    [Fact]
    public async Task Editing_requires_a_reason()
    {
        var (db, org, user) = NewContext(nameof(Editing_requires_a_reason));
        await using var _ = db;
        var sale = SeedSale(db, org);
        var controller = new SalesController(db, new StubCurrentUser(org, user));

        var result = await controller.Update(sale.Id, new UpdateSaleRequest(
            "eBay", 120m, 16m, 3.80m, 8m, 0m, 0m, 9.90m, true, sale.SoldAt, null, "  "), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.SaleCorrections.ToListAsync());
    }
}
