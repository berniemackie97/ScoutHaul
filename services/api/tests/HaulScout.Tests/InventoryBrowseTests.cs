using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Inventory;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
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
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static InventoryController NewController(HaulScoutDbContext db, Guid org, Guid user)
    {
        StubCurrentUser currentUser = new(org, user);
        InventoryReferenceDataService referenceData = new(new EfInventoryReferenceDataStore(db, currentUser));
        return new InventoryController(
            referenceData,
            new InventoryBundleWorkflowService(db, currentUser),
            new InventoryDeletionWorkflowService(db, currentUser),
            new InventoryUpsertWorkflowService(db, currentUser, referenceData),
            new InventoryQueryService(new EfInventoryStore(db), currentUser),
            null!,
            new InventoryIssueWorkflowService(new EfInventoryStore(db), new EfInventoryIssueStore(db), currentUser),
            new InventoryStatusWorkflowService(new EfInventoryStore(db), currentUser, TimeProvider.System));
    }

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
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Browse_pages_and_summarizes_the_whole_filtered_set));
        await using HaulScoutDbContext _ = db;
        for (int i = 0; i < 7; i++) SeedItem(db, org, $"Item {i}", InventoryStatus.Listed, 10m);
        SeedItem(db, org, "Sold Thing", InventoryStatus.Sold, 20m);
        InventoryController controller = NewController(db, org, user);

        ActionResult<InventoryListDto> result = await controller.Browse(null, null, includeArchived: false, null, null, true, page: 1, pageSize: 5, CancellationToken.None);

        InventoryListDto dto = result.Value!;
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
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Browse_searches_title_and_brand));
        await using HaulScoutDbContext _ = db;
        SeedItem(db, org, "Denim Jacket", InventoryStatus.Listed, 10m, brand: "Levi's");
        SeedItem(db, org, "Coffee Mug", InventoryStatus.Listed, 5m, brand: "Generic");
        InventoryController controller = NewController(db, org, user);

        ActionResult<InventoryListDto> byTitle = await controller.Browse(null, null, false, "denim", null, true, 1, 25, CancellationToken.None);
        Assert.Single(byTitle.Value!.Items);

        ActionResult<InventoryListDto> byBrand = await controller.Browse(null, null, false, "levi", null, true, 1, 25, CancellationToken.None);
        Assert.Single(byBrand.Value!.Items);
        Assert.Equal("Denim Jacket", byBrand.Value.Items[0].Title);
    }

    [Fact]
    public async Task Browse_sorts_by_purchase_price_ascending()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Browse_sorts_by_purchase_price_ascending));
        await using HaulScoutDbContext _ = db;
        SeedItem(db, org, "Cheap", InventoryStatus.Listed, 5m);
        SeedItem(db, org, "Pricey", InventoryStatus.Listed, 99m);
        InventoryController controller = NewController(db, org, user);

        ActionResult<InventoryListDto> result = await controller.Browse(null, null, false, null, "purchasePrice", desc: false, 1, 25, CancellationToken.None);

        Assert.Equal("Cheap", result.Value!.Items[0].Title);
        Assert.Equal("Pricey", result.Value.Items[1].Title);
    }
}
