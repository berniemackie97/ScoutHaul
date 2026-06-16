using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Controllers;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Common;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Tests;

public class StorePublishingTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (ScoutHaulDbContext Db, Guid Org) NewContext(string store)
    {
        var org = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ScoutHaulDbContext>()
            .UseInMemoryDatabase(store)
            .Options;
        var tenant = new TenantContext();
        tenant.SetOrganization(org);
        return (new ScoutHaulDbContext(options, tenant), org);
    }

    [Fact]
    public async Task Publishing_an_inventory_item_creates_a_listing_and_advances_the_item()
    {
        var (db, org) = NewContext(nameof(Publishing_an_inventory_item_creates_a_listing_and_advances_the_item));
        await using var _ = db;

        var item = new InventoryItem { Title = "Vintage Brass Lamp" };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        var controller = new StoreController(db, new StubCurrentUser(org));
        var request = new PublishListingRequest(
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

        var result = await controller.Publish(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<StoreListingDto>(created.Value);
        Assert.Equal("vintage-brass-lamp", dto.Slug);
        Assert.Equal(49.99m, dto.ListPrice);
        Assert.Equal(StoreListingStatus.Active, dto.Status);
        Assert.Equal(item.Id, dto.InventoryItemId);

        var reloaded = await db.InventoryItems.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(InventoryStatus.Listed, reloaded.Status);
        Assert.NotNull(reloaded.ListedAt);
    }

    [Fact]
    public async Task Publishing_the_same_item_twice_is_rejected()
    {
        var (db, org) = NewContext(nameof(Publishing_the_same_item_twice_is_rejected));
        await using var _ = db;

        var item = new InventoryItem { Title = "Record Player" };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        var controller = new StoreController(db, new StubCurrentUser(org));
        PublishListingRequest Request() => new(item.Id, 120m, null, null, null, null, null, false, null, null);

        var first = await controller.Publish(Request(), CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(first.Result);

        var second = await controller.Publish(Request(), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Fact]
    public async Task Listing_slugs_are_unique_within_the_store()
    {
        var (db, org) = NewContext(nameof(Listing_slugs_are_unique_within_the_store));
        await using var _ = db;

        var itemA = new InventoryItem { Title = "Oak Chair" };
        var itemB = new InventoryItem { Title = "Oak Chair" };
        db.InventoryItems.AddRange(itemA, itemB);
        await db.SaveChangesAsync();

        var controller = new StoreController(db, new StubCurrentUser(org));

        var a = await controller.Publish(new PublishListingRequest(itemA.Id, 30m, null, null, null, null, null, false, null, null), CancellationToken.None);
        var b = await controller.Publish(new PublishListingRequest(itemB.Id, 30m, null, null, null, null, null, false, null, null), CancellationToken.None);

        var slugA = ((StoreListingDto)((CreatedAtActionResult)a.Result!).Value!).Slug;
        var slugB = ((StoreListingDto)((CreatedAtActionResult)b.Result!).Value!).Slug;

        Assert.Equal("oak-chair", slugA);
        Assert.Equal("oak-chair-2", slugB);
    }
}
