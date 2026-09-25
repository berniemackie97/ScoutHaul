using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Storefront;

using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Tests;

public class StorefrontCatalogTests
{
    private static StorefrontController NewController(HaulScoutDbContext db, ITenantContext tenant, Guid? storeHeader)
    {
        StorefrontController controller = new(
            new StorefrontStoreResolver(new EfStoreDirectory(db), tenant),
            new StorefrontCatalogService(new EfStorefrontCatalogStore(db), new DatabaseInventoryPhotoBlobStore()));
        DefaultHttpContext httpContext = new();
        if (storeHeader is Guid id)
        {
            httpContext.Request.Headers["X-HaulScout-Store"] = id.ToString();
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task Browse_returns_only_shoppable_listings_for_the_resolved_store()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Browse_returns_only_shoppable_listings_for_the_resolved_store))
            .Options;

        TenantContext seedTenant = new();
        seedTenant.SetOrganization(org);
        await using (HaulScoutDbContext seed = new(options, seedTenant))
        {
            seed.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading" });
            // Sellable: listed + priced.
            seed.InventoryItems.Add(new InventoryItem { Title = "Listed item", Status = InventoryStatus.Listed, Quantity = 1, EstimatedSalePrice = 25m, Photos = { new InventoryPhoto { ContentType = "image/jpeg", OriginalBytes = [1], ThumbnailBytes = [1] } } });
            // Sellable: listed + priced even at Quantity 0 (operators use 0 for single, untracked items).
            seed.InventoryItems.Add(new InventoryItem { Title = "Listed single", Status = InventoryStatus.Listed, Quantity = 0, EstimatedSalePrice = 30m, Photos = { new InventoryPhoto { ContentType = "image/jpeg", OriginalBytes = [1], ThumbnailBytes = [1] } } });
            // Not sellable: still a draft.
            seed.InventoryItems.Add(new InventoryItem { Title = "Draft item", Status = InventoryStatus.Draft, Quantity = 1, EstimatedSalePrice = 20m });
            // Not sellable: listed but unpriced.
            seed.InventoryItems.Add(new InventoryItem { Title = "Unpriced", Status = InventoryStatus.Listed, Quantity = 1 });
            // Not sellable: quarantined authenticity or customer-owned repair work.
            seed.InventoryItems.Add(new InventoryItem { Title = "Counterfeit", Status = InventoryStatus.Listed, EstimatedSalePrice = 100m, AuthenticityStatus = InventoryAuthenticityStatus.Counterfeit });
            seed.InventoryItems.Add(new InventoryItem { Title = "Customer repair", Status = InventoryStatus.Listed, EstimatedSalePrice = 100m, OwnershipType = InventoryOwnershipType.CustomerRepair });
            await seed.SaveChangesAsync();
        }

        // Read context shares ONE tenant instance with the controller (as in request DI scope);
        // the controller resolves the store from the header and sets it.
        TenantContext tenant = new();
        await using HaulScoutDbContext db = new(options, tenant);
        StorefrontController controller = NewController(db, tenant, org);

        ActionResult<StorefrontPageDto<StorefrontListingDto>> result = await controller.Browse(category: null, search: null, page: 1, pageSize: 24);

        Assert.Null(result.Result);
        StorefrontPageDto<StorefrontListingDto>? page = result.Value!;
        Assert.Equal(2, page.Total);
        Assert.Contains(page.Items, x => x.Title == "Listed item");
        Assert.Contains(page.Items, x => x.Title == "Listed single");
        Assert.All(page.Items, x => Assert.True(x.ListPrice > 0));
    }

    [Fact]
    public async Task Detail_exposes_safe_structured_specs_without_private_identifiers()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Detail_exposes_safe_structured_specs_without_private_identifiers))
            .Options;
        TenantContext seedTenant = new();
        seedTenant.SetOrganization(org);
        await using (HaulScoutDbContext seed = new(options, seedTenant))
        {
            seed.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading" });
            seed.InventoryItems.Add(new InventoryItem
            {
                Title = "Nintendo Switch OLED",
                Status = InventoryStatus.Listed,
                EstimatedSalePrice = 250m,
                Brand = "Nintendo",
                Model = "Switch OLED",
                Color = "White",
                FunctionalStatus = InventoryFunctionalStatus.FullyFunctional,
                CompletenessStatus = InventoryCompletenessStatus.Complete,
                PackagingStatus = InventoryPackagingStatus.OriginalExcellent,
                SerialNumber = "PRIVATE-SERIAL",
                Weight = 3.2m,
                WeightUnit = WeightUnit.Pounds,
                Photos = { new InventoryPhoto { ContentType = "image/jpeg", OriginalBytes = [1], ThumbnailBytes = [1] } },
            });
            await seed.SaveChangesAsync();
        }

        TenantContext tenant = new();
        await using HaulScoutDbContext db = new(options, tenant);
        StorefrontController controller = NewController(db, tenant, org);
        ActionResult<StorefrontPageDto<StorefrontListingDto>> browse = await controller.Browse(category: null, search: null, page: 1, pageSize: 24);
        ActionResult<StorefrontListingDetailDto> detail = await controller.Detail(browse.Value!.Items.Single().Slug, CancellationToken.None);

        Assert.Equal("Switch OLED", detail.Value!.Model);
        Assert.Equal(InventoryFunctionalStatus.FullyFunctional, detail.Value.FunctionalStatus);
        Assert.Equal(InventoryCompletenessStatus.Complete, detail.Value.CompletenessStatus);
        Assert.Equal(3.2m, detail.Value.Weight);
    }

    [Fact]
    public async Task Browse_rejects_an_unknown_store()
    {
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Browse_rejects_an_unknown_store))
            .Options;

        TenantContext tenant = new();
        await using HaulScoutDbContext db = new(options, tenant);
        StorefrontController controller = NewController(db, tenant, Guid.NewGuid()); // header points at a store that doesn't exist

        ActionResult<StorefrontPageDto<StorefrontListingDto>> result = await controller.Browse(category: null, search: null, page: 1, pageSize: 24);

        ProblemAssert.Invalid(result.Result);
    }
}
