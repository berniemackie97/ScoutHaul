using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Channels;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Channels;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// An item that sold on one marketplace and is still for sale on another. Recording the sale has always flagged
/// those listings; nothing ever showed them to anyone or let them be cleared, so the flag piled up unread.
///
/// Nothing here removes a listing from a marketplace: doing that with software is against their terms. The queue
/// finds them, and records what the operator did about them.
/// </summary>
public sealed class DelistQueueTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed record Harness(HaulScoutDbContext Db, ChannelDelistService Delisting, Guid Org, ChannelListing Listing);

    private static Harness NewHarness(string name, ChannelListingStatus status = ChannelListingStatus.PendingDelist)
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(name).Options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });

        InventoryItem item = new() { OrganizationId = org, Title = "Carhartt Detroit Jacket", Status = InventoryStatus.Sold };
        ChannelAccount ebay = new() { OrganizationId = org, Channel = "eBay", DisplayName = "overhaultradingco (ebay)" };
        ChannelListing listing = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            InventoryItem = item,
            ChannelAccountId = ebay.Id,
            ChannelAccount = ebay,
            Status = status,
            ExternalListingId = "377296454991",
            ExternalUrl = "https://www.ebay.com/itm/377296454991",
            ListedPrice = 80m,
        };
        db.InventoryItems.Add(item);
        db.ChannelAccounts.Add(ebay);
        db.ChannelListings.Add(listing);
        db.Sales.Add(new Sale
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "Vinted",
            SoldPrice = 75m,
            SoldAt = DateTimeOffset.UtcNow.AddDays(-1),
            Status = SaleStatus.Recorded,
        });
        db.SaveChanges();

        StubCurrentUser currentUser = new(org);
        return new Harness(db, new ChannelDelistService(new EfChannelDelistStore(db), currentUser, TimeProvider.System), org, listing);
    }

    [Fact]
    public async Task The_queue_says_what_is_still_for_sale_and_why()
    {
        Harness harness = NewHarness(nameof(The_queue_says_what_is_still_for_sale_and_why));
        await using HaulScoutDbContext _ = harness.Db;

        PendingDelistDto pending = Assert.Single(await harness.Delisting.ListAsync(CancellationToken.None));

        Assert.Equal("Carhartt Detroit Jacket", pending.ItemTitle);
        Assert.Equal("eBay", pending.Channel);
        // The link is the point: the operator ends the listing on the marketplace itself.
        Assert.Equal("https://www.ebay.com/itm/377296454991", pending.ExternalUrl);
        Assert.Equal(80m, pending.ListedPrice);
        Assert.Equal("Vinted", pending.SoldOnChannel);
        Assert.NotNull(pending.SoldAt);
        Assert.Equal(1, await harness.Delisting.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Saying_it_is_down_clears_it_from_the_queue()
    {
        Harness harness = NewHarness(nameof(Saying_it_is_down_clears_it_from_the_queue));
        await using HaulScoutDbContext _ = harness.Db;

        AppResult<PendingDelistDto> result = await harness.Delisting.MarkDelistedAsync(harness.Listing.Id, CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        Assert.Equal(ChannelListingStatus.Delisted, (await harness.Db.ChannelListings.SingleAsync()).Status);
        Assert.Empty(await harness.Delisting.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Leaving_it_listed_puts_it_back_on_sale_rather_than_asking_again()
    {
        Harness harness = NewHarness(nameof(Leaving_it_listed_puts_it_back_on_sale_rather_than_asking_again));
        await using HaulScoutDbContext _ = harness.Db;

        await harness.Delisting.KeepListedAsync(harness.Listing.Id, CancellationToken.None);

        Assert.Equal(ChannelListingStatus.Active, (await harness.Db.ChannelListings.SingleAsync()).Status);
        Assert.Empty(await harness.Delisting.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_listing_someone_else_already_answered_is_not_answered_twice()
    {
        Harness harness = NewHarness(nameof(A_listing_someone_else_already_answered_is_not_answered_twice));
        await using HaulScoutDbContext _ = harness.Db;
        await harness.Delisting.MarkDelistedAsync(harness.Listing.Id, CancellationToken.None);

        AppResult<PendingDelistDto> again = await harness.Delisting.KeepListedAsync(harness.Listing.Id, CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, again.Status);
        Assert.Equal(ChannelListingStatus.Delisted, (await harness.Db.ChannelListings.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_listing_that_is_simply_live_is_not_in_the_queue()
    {
        Harness harness = NewHarness(nameof(A_listing_that_is_simply_live_is_not_in_the_queue), ChannelListingStatus.Active);
        await using HaulScoutDbContext _ = harness.Db;

        Assert.Empty(await harness.Delisting.ListAsync(CancellationToken.None));
        Assert.Equal(AppResultStatus.Validation,
            (await harness.Delisting.MarkDelistedAsync(harness.Listing.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_cancelled_sale_is_not_the_reason_a_listing_is_still_up()
    {
        Harness harness = NewHarness(nameof(A_cancelled_sale_is_not_the_reason_a_listing_is_still_up));
        await using HaulScoutDbContext _ = harness.Db;
        Sale sale = await harness.Db.Sales.SingleAsync();
        sale.Status = SaleStatus.Cancelled;
        await harness.Db.SaveChangesAsync();

        PendingDelistDto pending = Assert.Single(await harness.Delisting.ListAsync(CancellationToken.None));

        // The listing is still flagged, but the queue must not blame a sale that was undone.
        Assert.Null(pending.SoldOnChannel);
        Assert.Null(pending.SoldAt);
    }
}
