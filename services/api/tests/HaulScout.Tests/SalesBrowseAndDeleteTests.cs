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

public class SalesBrowseAndDeleteTests
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

    private static Sale SeedSale(HaulScoutDbContext db, Guid org, string title, decimal price, decimal net, DateTimeOffset soldAt, string platform = "eBay")
    {
        InventoryItem item = new() { OrganizationId = org, Title = title, PurchasePrice = 10m, Status = InventoryStatus.Sold, SoldAt = soldAt };
        db.InventoryItems.Add(item);
        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = platform,
            SoldPrice = price,
            CostOfGoods = 10m,
            NetProfit = net,
            Status = SaleStatus.Delivered,
            SoldAt = soldAt,
            InventoryItem = item,
        };
        db.Sales.Add(sale);
        db.SaveChanges();
        return sale;
    }

    [Fact]
    public async Task Browse_pages_and_reports_total_and_summary()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Browse_pages_and_reports_total_and_summary));
        await using HaulScoutDbContext _ = db;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++) SeedSale(db, org, $"Item {i}", 100m, 40m, now.AddDays(-i));
        SalesController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<SaleListDto> result = await controller.Browse(status: null, search: null, sort: null, desc: true, page: 1, pageSize: 2, CancellationToken.None);

        SaleListDto dto = result.Value!;
        Assert.Equal(5, dto.Total);
        Assert.Equal(3, dto.PageCount);
        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(5, dto.Summary.RealizedCount);
        Assert.Equal(200m, dto.Summary.NetProfit);       // 5 × 40
        Assert.Equal(500m, dto.Summary.ItemSales);       // 5 × 100
    }

    [Fact]
    public async Task Browse_searches_across_title_platform_and_order_reference()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Browse_searches_across_title_platform_and_order_reference));
        await using HaulScoutDbContext _ = db;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SeedSale(db, org, "Brass Lamp", 100m, 40m, now);
        SeedSale(db, org, "Walnut Desk", 200m, 80m, now, platform: "Mercari");
        SalesController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<SaleListDto> byTitle = await controller.Browse(null, "brass", null, true, 1, 25, CancellationToken.None);
        Assert.Single(byTitle.Value!.Items);
        Assert.Equal("Brass Lamp", byTitle.Value.Items[0].InventoryItemTitle);

        ActionResult<SaleListDto> byPlatform = await controller.Browse(null, "mercari", null, true, 1, 25, CancellationToken.None);
        Assert.Single(byPlatform.Value!.Items);
    }

    [Fact]
    public async Task Browse_sorts_by_net_profit_ascending()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Browse_sorts_by_net_profit_ascending));
        await using HaulScoutDbContext _ = db;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SeedSale(db, org, "Low", 100m, 10m, now);
        SeedSale(db, org, "High", 100m, 90m, now.AddSeconds(-1));
        SalesController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<SaleListDto> result = await controller.Browse(null, null, "netProfit", desc: false, 1, 25, CancellationToken.None);

        Assert.Equal(10m, result.Value!.Items[0].NetProfit);
        Assert.Equal(90m, result.Value.Items[1].NetProfit);
    }

    [Fact]
    public async Task Deleting_a_sale_releases_inventory_removes_the_comp_and_audits()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Deleting_a_sale_releases_inventory_removes_the_comp_and_audits));
        await using HaulScoutDbContext _ = db;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Sale sale = SeedSale(db, org, "Brass Lamp", 100m, 40m, now);
        db.MarketObservations.Add(new MarketObservation
        {
            OrganizationId = org,
            InventoryItemId = sale.InventoryItemId,
            ObservationType = MarketObservationType.InternalSale,
            Source = "Internal",
            Title = "Brass Lamp",
            Price = 100m,
            TotalPrice = 100m,
            ObservedAt = now,
            SoldAt = now,
        });
        db.Refunds.Add(new Refund { OrganizationId = org, SaleId = sale.Id, ItemAmount = 5m, Reason = "partial", Sale = sale, RecordedByUserId = user });
        await db.SaveChangesAsync();
        SalesController controller = new(db, new StubCurrentUser(org, user));

        ActionResult result = await controller.Delete(sale.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Empty(await db.Refunds.ToListAsync());                         // dependent refund gone
        Assert.Empty(await db.MarketObservations.ToListAsync());              // internal comp removed
        InventoryItem item = await db.InventoryItems.SingleAsync(i => i.Id == sale.InventoryItemId);
        Assert.Equal(InventoryStatus.ReadyToList, item.Status);              // released back to inventory
        Assert.Null(item.SoldAt);
        Assert.Equal(1, await db.AuditLogs.CountAsync(log => log.Action == "Deleted" && log.EntityId == sale.Id));
    }

    [Fact]
    public async Task Deleting_a_missing_sale_returns_not_found()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Deleting_a_missing_sale_returns_not_found));
        await using HaulScoutDbContext _ = db;
        SalesController controller = new(db, new StubCurrentUser(org, user));

        Assert.IsType<NotFoundResult>(await controller.Delete(Guid.NewGuid(), CancellationToken.None));
    }
}
