using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class InventoryBrowseTests
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
        db.SaveChanges();
        return (db, org, user);
    }

    private static InventoryController NewController(HaulScoutDbContext db, Guid org, Guid user) =>
        new(db, new StubCurrentUser(org, user), null!, null!);

    private static void SeedItem(HaulScoutDbContext db, Guid org, string title, InventoryStatus status, decimal cost, string? brand = null)
    {
        db.InventoryItems.Add(new InventoryItem
        {
            OrganizationId = org,
            Title = title,
            Status = status,
            PurchasePrice = cost,
            EstimatedSalePrice = cost * 3,
            Brand = brand,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Browse_pages_and_summarizes_the_whole_filtered_set()
    {
        var (db, org, user) = NewContext(nameof(Browse_pages_and_summarizes_the_whole_filtered_set));
        await using var _ = db;
        for (int i = 0; i < 7; i++) SeedItem(db, org, $"Item {i}", InventoryStatus.Listed, 10m);
        SeedItem(db, org, "Sold Thing", InventoryStatus.Sold, 20m);
        var controller = NewController(db, org, user);

        var result = await controller.Browse(null, null, includeArchived: false, null, null, true, page: 1, pageSize: 5, CancellationToken.None);

        var dto = result.Value!;
        Assert.Equal(8, dto.Total);
        Assert.Equal(2, dto.PageCount);
        Assert.Equal(5, dto.Items.Count);
        Assert.Equal(8, dto.Summary.Total);
        Assert.Equal(1, dto.Summary.Sold);
        Assert.Equal(7, dto.Summary.ReadyOrListed);
        Assert.Equal(90m, dto.Summary.CapitalDeployed); // 7×10 + 20
    }

    [Fact]
    public async Task Browse_searches_title_and_brand()
    {
        var (db, org, user) = NewContext(nameof(Browse_searches_title_and_brand));
        await using var _ = db;
        SeedItem(db, org, "Denim Jacket", InventoryStatus.Listed, 10m, brand: "Levi's");
        SeedItem(db, org, "Coffee Mug", InventoryStatus.Listed, 5m, brand: "Generic");
        var controller = NewController(db, org, user);

        var byTitle = await controller.Browse(null, null, false, "denim", null, true, 1, 25, CancellationToken.None);
        Assert.Single(byTitle.Value!.Items);

        var byBrand = await controller.Browse(null, null, false, "levi", null, true, 1, 25, CancellationToken.None);
        Assert.Single(byBrand.Value!.Items);
        Assert.Equal("Denim Jacket", byBrand.Value.Items[0].Title);
    }

    [Fact]
    public async Task Browse_sorts_by_purchase_price_ascending()
    {
        var (db, org, user) = NewContext(nameof(Browse_sorts_by_purchase_price_ascending));
        await using var _ = db;
        SeedItem(db, org, "Cheap", InventoryStatus.Listed, 5m);
        SeedItem(db, org, "Pricey", InventoryStatus.Listed, 99m);
        var controller = NewController(db, org, user);

        var result = await controller.Browse(null, null, false, null, "purchasePrice", desc: false, 1, 25, CancellationToken.None);

        Assert.Equal("Cheap", result.Value!.Items[0].Title);
        Assert.Equal("Pricey", result.Value.Items[1].Title);
    }
}
