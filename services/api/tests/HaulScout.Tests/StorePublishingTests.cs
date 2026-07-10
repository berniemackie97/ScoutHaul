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

public class StorePublishingTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store)
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        return (new HaulScoutDbContext(options, tenant), org);
    }

    [Fact]
    public async Task Publishing_an_inventory_item_creates_a_listing_and_advances_the_item()
    {
        (HaulScoutDbContext db, Guid org) = NewContext(nameof(Publishing_an_inventory_item_creates_a_listing_and_advances_the_item));
        await using HaulScoutDbContext _ = db;

        InventoryItem item = new() { Title = "Vintage Brass Lamp" };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        StoreController controller = new(db, new StubCurrentUser(org));
        PublishListingRequest request = new(
            InventoryItemId: item.Id,
            ListPrice: 49.99m,
            StoreCategoryId: null,
            Title: null,
            Subtitle: null,
            Description: null,
            CompareAtPrice: null,
            IsFeatured: false,
            MetaTitle: null,
            MetaDescription: null);

        ActionResult<StoreListingDto> result = await controller.Publish(request, CancellationToken.None);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        StoreListingDto dto = Assert.IsType<StoreListingDto>(created.Value);
        Assert.Equal("vintage-brass-lamp", dto.Slug);
        Assert.Equal(49.99m, dto.ListPrice);
        Assert.Equal(StoreListingStatus.Active, dto.Status);
        Assert.Equal(item.Id, dto.InventoryItemId);

        InventoryItem reloaded = await db.InventoryItems.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(InventoryStatus.Listed, reloaded.Status);
        Assert.NotNull(reloaded.ListedAt);
    }

    [Fact]
    public async Task Publishing_the_same_item_twice_is_rejected()
    {
        (HaulScoutDbContext db, Guid org) = NewContext(nameof(Publishing_the_same_item_twice_is_rejected));
        await using HaulScoutDbContext _ = db;

        InventoryItem item = new() { Title = "Record Player" };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        StoreController controller = new(db, new StubCurrentUser(org));
        PublishListingRequest Request() => new(item.Id, 120m, null, null, null, null, null, false, null, null);

        ActionResult<StoreListingDto> first = await controller.Publish(Request(), CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(first.Result);

        ActionResult<StoreListingDto> second = await controller.Publish(Request(), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Fact]
    public async Task Listing_slugs_are_unique_within_the_store()
    {
        (HaulScoutDbContext db, Guid org) = NewContext(nameof(Listing_slugs_are_unique_within_the_store));
        await using HaulScoutDbContext _ = db;

        InventoryItem itemA = new() { Title = "Oak Chair" };
        InventoryItem itemB = new() { Title = "Oak Chair" };
        db.InventoryItems.AddRange(itemA, itemB);
        await db.SaveChangesAsync();

        StoreController controller = new(db, new StubCurrentUser(org));

        ActionResult<StoreListingDto> a = await controller.Publish(new PublishListingRequest(itemA.Id, 30m, null, null, null, null, null, false, null, null), CancellationToken.None);
        ActionResult<StoreListingDto> b = await controller.Publish(new PublishListingRequest(itemB.Id, 30m, null, null, null, null, null, false, null, null), CancellationToken.None);

        string slugA = ((StoreListingDto)((CreatedAtActionResult)a.Result!).Value!).Slug;
        string slugB = ((StoreListingDto)((CreatedAtActionResult)b.Result!).Value!).Slug;

        Assert.Equal("oak-chair", slugA);
        Assert.Equal("oak-chair-2", slugB);
    }
}
