using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class StorefrontCatalogTests
{
    private static StorefrontController NewController(HaulScoutDbContext db, ITenantContext tenant, Guid? storeHeader)
    {
        var controller = new StorefrontController(db, tenant);
        var httpContext = new DefaultHttpContext();
        if (storeHeader is Guid id)
        {
            httpContext.Request.Headers["X-HaulScout-Store"] = id.ToString();
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task Browse_returns_only_active_listings_for_the_resolved_store()
    {
        var org = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Browse_returns_only_active_listings_for_the_resolved_store))
            .Options;

        var seedTenant = new TenantContext();
        seedTenant.SetOrganization(org);
        await using (var seed = new HaulScoutDbContext(options, seedTenant))
        {
            seed.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading" });
            var active = new InventoryItem { Title = "Active item" };
            var draft = new InventoryItem { Title = "Draft item" };
            seed.InventoryItems.AddRange(active, draft);
            seed.StoreListings.Add(new StoreListing { InventoryItemId = active.Id, Slug = "active-item", Title = "Active item", ListPrice = 10m, Status = StoreListingStatus.Active });
            seed.StoreListings.Add(new StoreListing { InventoryItemId = draft.Id, Slug = "draft-item", Title = "Draft item", ListPrice = 20m, Status = StoreListingStatus.Draft });
            await seed.SaveChangesAsync();
        }

        // Read context shares ONE tenant instance with the controller (as in request DI scope);
        // the controller resolves the store from the header and sets it.
        var tenant = new TenantContext();
        await using var db = new HaulScoutDbContext(options, tenant);
        var controller = NewController(db, tenant, org);

        var result = await controller.Browse(category: null, search: null, page: 1, pageSize: 24);

        Assert.Null(result.Result);
        var page = result.Value!;
        var only = Assert.Single(page.Items);
        Assert.Equal("active-item", only.Slug);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Browse_rejects_an_unknown_store()
    {
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Browse_rejects_an_unknown_store))
            .Options;

        var tenant = new TenantContext();
        await using var db = new HaulScoutDbContext(options, tenant);
        var controller = NewController(db, tenant, Guid.NewGuid()); // header points at a store that doesn't exist

        var result = await controller.Browse(category: null, search: null, page: 1, pageSize: 24);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
