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

public class ChannelAccountTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, ChannelAccountsController Controller, Guid Org) NewController([System.Runtime.CompilerServices.CallerMemberName] string store = "")
    {
        var org = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        var tenant = new TenantContext();
        tenant.SetOrganization(org);
        var db = new HaulScoutDbContext(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, new ChannelAccountsController(db, new StubCurrentUser(org, Guid.NewGuid())), org);
    }

    private static UpsertChannelAccountRequest Request(string channel, string name) =>
        new(channel, name, ExternalAccountId: null, DefaultFeeProfileId: null, IsActive: true, Notes: null);

    private static ChannelAccountDto Created(ActionResult<ChannelAccountDto> result) =>
        Assert.IsType<ChannelAccountDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);

    [Fact]
    public async Task Storefront_is_native_and_marketplace_is_external()
    {
        var (_, controller, _) = NewController();

        var storefront = Created(await controller.Create(Request("Storefront", "Own storefront"), default));
        var ebay = Created(await controller.Create(Request("eBay", "Overhaul eBay Store"), default));

        Assert.True(storefront.IsNativelySynced);
        Assert.False(ebay.IsNativelySynced);
        Assert.Equal("eBay", ebay.Channel);
    }

    [Fact]
    public async Task Unknown_channel_is_rejected()
    {
        var (_, controller, _) = NewController();
        var result = await controller.Create(Request("MySpaceMarket", "Nope"), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Duplicate_display_name_conflicts()
    {
        var (_, controller, _) = NewController();
        await controller.Create(Request("eBay", "Main Store"), default);

        var duplicate = await controller.Create(Request("Poshmark", "Main Store"), default);
        Assert.IsType<ConflictObjectResult>(duplicate.Result);
    }

    [Fact]
    public async Task Delete_blocks_when_listings_exist_but_allows_when_empty()
    {
        var (db, controller, org) = NewController();
        var empty = Created(await controller.Create(Request("eBay", "Empty Store"), default));
        var used = Created(await controller.Create(Request("Poshmark", "Busy Closet"), default));

        var item = new InventoryItem { OrganizationId = org, Title = "Denim jacket" };
        db.InventoryItems.Add(item);
        db.ChannelListings.Add(new ChannelListing
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            ChannelAccountId = used.Id,
            Status = ChannelListingStatus.Active,
        });
        await db.SaveChangesAsync();

        Assert.IsType<ConflictObjectResult>(await controller.Delete(used.Id, default));
        Assert.IsType<NoContentResult>(await controller.Delete(empty.Id, default));
    }
}
