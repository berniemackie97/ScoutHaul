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

public class ChannelListingTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, ChannelListingsController Controller, Guid Org, Guid Item, Guid Account) Setup([System.Runtime.CompilerServices.CallerMemberName] string store = "")
    {
        var org = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        var tenant = new TenantContext();
        tenant.SetOrganization(org);
        var db = new HaulScoutDbContext(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        var item = new InventoryItem { OrganizationId = org, Title = "Camera" };
        var account = new ChannelAccount { OrganizationId = org, Channel = "eBay", DisplayName = "Main Store" };
        db.InventoryItems.Add(item);
        db.ChannelAccounts.Add(account);
        db.SaveChanges();
        return (db, new ChannelListingsController(db, new StubCurrentUser(org, Guid.NewGuid())), org, item.Id, account.Id);
    }

    private static UpsertChannelListingRequest Req(Guid account, ChannelListingStatus status, decimal? price = null, string? url = null) =>
        new(account, status, price, Quantity: null, ExternalListingId: null, ExternalUrl: url, ImageHash: null);

    [Fact]
    public async Task Upsert_creates_then_updates_a_single_row_per_account()
    {
        var (db, controller, _, item, account) = Setup();

        var created = (await controller.Upsert(item, Req(account, ChannelListingStatus.Active, 120m, "https://ebay/1"), default)).Value!;
        Assert.Equal(ChannelListingStatus.Active, created.Status);
        Assert.NotNull(created.ListedAt);
        Assert.Equal(120m, created.ListedPrice);

        var updated = (await controller.Upsert(item, Req(account, ChannelListingStatus.Sold, 130m), default)).Value!;
        Assert.Equal(ChannelListingStatus.Sold, updated.Status);
        Assert.NotNull(updated.SoldAt);

        // Same item + account = one row, not two.
        Assert.Equal(1, await db.ChannelListings.CountAsync(listing => listing.InventoryItemId == item));
    }

    [Fact]
    public async Task Upsert_rejects_unknown_account()
    {
        var (_, controller, _, item, _) = Setup();
        var result = await controller.Upsert(item, Req(Guid.NewGuid(), ChannelListingStatus.Active), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_removes_the_listing()
    {
        var (db, controller, _, item, account) = Setup();
        var created = (await controller.Upsert(item, Req(account, ChannelListingStatus.Active), default)).Value!;

        Assert.IsType<NoContentResult>(await controller.Delete(item, created.Id, default));
        Assert.Equal(0, await db.ChannelListings.CountAsync());
    }
}
