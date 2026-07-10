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
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        InventoryItem item = new() { OrganizationId = org, Title = "Camera" };
        ChannelAccount account = new() { OrganizationId = org, Channel = "eBay", DisplayName = "Main Store" };
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
        (HaulScoutDbContext db, ChannelListingsController controller, _, Guid item, Guid account) = Setup();

        ChannelListingDto created = (await controller.Upsert(item, Req(account, ChannelListingStatus.Active, 120m, "https://ebay/1"), default)).Value!;
        Assert.Equal(ChannelListingStatus.Active, created.Status);
        Assert.NotNull(created.ListedAt);
        Assert.Equal(120m, created.ListedPrice);

        ChannelListingDto updated = (await controller.Upsert(item, Req(account, ChannelListingStatus.Sold, 130m), default)).Value!;
        Assert.Equal(ChannelListingStatus.Sold, updated.Status);
        Assert.NotNull(updated.SoldAt);

        // Same item + account = one row, not two.
        Assert.Equal(1, await db.ChannelListings.CountAsync(listing => listing.InventoryItemId == item));
    }

    [Fact]
    public async Task Upsert_rejects_unknown_account()
    {
        (_, ChannelListingsController controller, _, Guid item, _) = Setup();
        ActionResult<ChannelListingDto> result = await controller.Upsert(item, Req(Guid.NewGuid(), ChannelListingStatus.Active), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_removes_the_listing()
    {
        (HaulScoutDbContext db, ChannelListingsController controller, _, Guid item, Guid account) = Setup();
        ChannelListingDto created = (await controller.Upsert(item, Req(account, ChannelListingStatus.Active), default)).Value!;

        Assert.IsType<NoContentResult>(await controller.Delete(item, created.Id, default));
        Assert.Equal(0, await db.ChannelListings.CountAsync());
    }

    [Fact]
    public async Task Selling_on_one_channel_flags_the_others_for_delist()
    {
        (HaulScoutDbContext db, ChannelListingsController controller, Guid org, Guid item, Guid ebay) = Setup();
        ChannelAccount posh = new() { OrganizationId = org, Channel = "Poshmark", DisplayName = "Closet" };
        db.ChannelAccounts.Add(posh);
        await db.SaveChangesAsync();

        await controller.Upsert(item, Req(ebay, ChannelListingStatus.Active), default);
        await controller.Upsert(item, Req(posh.Id, ChannelListingStatus.Active), default);

        // Sold on eBay → Poshmark's still-live listing must be flagged to come down.
        await controller.Upsert(item, Req(ebay, ChannelListingStatus.Sold), default);

        ChannelListing poshListing = await db.ChannelListings.SingleAsync(listing => listing.ChannelAccountId == posh.Id);
        Assert.Equal(ChannelListingStatus.PendingDelist, poshListing.Status);

        // The org-wide worklist surfaces it (with the item title) for the delist queue.
        IReadOnlyList<ChannelListingDto>? worklist = (await controller.Worklist("PendingDelist", default)).Value!;
        ChannelListingDto flagged = Assert.Single(worklist);
        Assert.Equal(posh.Id, flagged.ChannelAccountId);
        Assert.Equal("Camera", flagged.InventoryItemTitle);
    }
}
