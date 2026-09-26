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

/// <summary>A marketplace saying a listing sold while the books show its item unsold.</summary>
public sealed class SoldNotRecordedTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed record Harness(HaulScoutDbContext Db, SoldListingService Sold, Guid Org, InventoryItem Item, PlatformListing Listing);

    private static Harness NewHarness(string name, PlatformListingStatus status = PlatformListingStatus.Sold)
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(name).Options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });

        InventoryItem item = new() { OrganizationId = org, Title = "Ripple Junction Graphic Tee", Status = InventoryStatus.Listed };
        PlatformListing listing = new()
        {
            OrganizationId = org,
            Platform = "Poshmark",
            ExternalListingId = "6a4a90a11986c2a9de9f4f69",
            Title = "Ripple Junction Graphic Tee",
            Price = 15m,
            Status = status,
            Url = "https://poshmark.com/listing/6a4a90a11986c2a9de9f4f69",
            ReconcileStatus = PlatformListingReconcileStatus.Linked,
            ReconciledInventoryItemId = item.Id,
            ReconciledInventoryItem = item,
            MarkedSoldAt = status == PlatformListingStatus.Sold ? DateTimeOffset.UtcNow.AddHours(-3) : null,
        };
        db.InventoryItems.Add(item);
        db.PlatformListings.Add(listing);
        db.SaveChanges();

        StubCurrentUser currentUser = new(org);
        return new Harness(db, new SoldListingService(new EfSoldListingStore(db), currentUser, TimeProvider.System), org, item, listing);
    }

    [Fact]
    public async Task A_sold_listing_with_no_sale_behind_it_is_in_the_queue()
    {
        Harness harness = NewHarness(nameof(A_sold_listing_with_no_sale_behind_it_is_in_the_queue));
        await using HaulScoutDbContext _ = harness.Db;

        SoldNotRecordedDto pending = Assert.Single(await harness.Sold.ListAsync(CancellationToken.None));

        Assert.Equal("Ripple Junction Graphic Tee", pending.ItemTitle);
        Assert.Equal("Poshmark", pending.Platform);
        // What a listings scan knows, which is what the operator is offered when recording the sale.
        Assert.Equal(15m, pending.ListedPrice);
        Assert.NotNull(pending.MarkedSoldAt);
        Assert.Equal(1, await harness.Sold.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_sale_already_on_the_books_answers_for_the_listing()
    {
        Harness harness = NewHarness(nameof(A_sale_already_on_the_books_answers_for_the_listing));
        await using HaulScoutDbContext _ = harness.Db;
        harness.Db.Sales.Add(new Sale
        {
            OrganizationId = harness.Org,
            InventoryItemId = harness.Item.Id,
            Platform = "Poshmark",
            SoldPrice = 15m,
            SoldAt = DateTimeOffset.UtcNow,
            Status = SaleStatus.Recorded,
        });
        await harness.Db.SaveChangesAsync();

        Assert.Empty(await harness.Sold.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_sale_does_not_answer_for_it()
    {
        Harness harness = NewHarness(nameof(A_cancelled_sale_does_not_answer_for_it));
        await using HaulScoutDbContext _ = harness.Db;
        harness.Db.Sales.Add(new Sale
        {
            OrganizationId = harness.Org,
            InventoryItemId = harness.Item.Id,
            Platform = "Poshmark",
            SoldPrice = 15m,
            SoldAt = DateTimeOffset.UtcNow,
            Status = SaleStatus.Cancelled,
        });
        await harness.Db.SaveChangesAsync();

        Assert.Single(await harness.Sold.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Saying_it_is_dealt_with_clears_it_once()
    {
        Harness harness = NewHarness(nameof(Saying_it_is_dealt_with_clears_it_once));
        await using HaulScoutDbContext _ = harness.Db;

        Assert.Equal(AppResultStatus.Ok, (await harness.Sold.AcknowledgeAsync(harness.Listing.Id, CancellationToken.None)).Status);
        Assert.Empty(await harness.Sold.ListAsync(CancellationToken.None));

        AppResult<SoldNotRecordedDto> again = await harness.Sold.AcknowledgeAsync(harness.Listing.Id, CancellationToken.None);
        Assert.Equal(AppResultStatus.Validation, again.Status);
    }

    [Fact]
    public async Task A_listing_that_is_still_for_sale_is_not_in_the_queue()
    {
        Harness harness = NewHarness(nameof(A_listing_that_is_still_for_sale_is_not_in_the_queue), PlatformListingStatus.Active);
        await using HaulScoutDbContext _ = harness.Db;

        Assert.Empty(await harness.Sold.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_listing_the_queue_is_not_asking_about_cannot_be_answered()
    {
        // Otherwise an answer given to a listing that is still for sale would silently bury its next sale.
        Harness harness = NewHarness(nameof(A_listing_the_queue_is_not_asking_about_cannot_be_answered), PlatformListingStatus.Active);
        await using HaulScoutDbContext _ = harness.Db;

        AppResult<SoldNotRecordedDto> refused = await harness.Sold.AcknowledgeAsync(harness.Listing.Id, CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, refused.Status);
        Assert.Null((await harness.Db.PlatformListings.SingleAsync()).SoldAcknowledgedAt);
    }

    [Fact]
    public async Task A_sold_listing_with_no_item_behind_it_cannot_be_answered_either()
    {
        Harness harness = NewHarness(nameof(A_sold_listing_with_no_item_behind_it_cannot_be_answered_either));
        await using HaulScoutDbContext _ = harness.Db;
        PlatformListing listing = await harness.Db.PlatformListings.SingleAsync();
        listing.ReconciledInventoryItemId = null;
        listing.ReconcileStatus = PlatformListingReconcileStatus.Pending;
        await harness.Db.SaveChangesAsync();

        Assert.Equal(AppResultStatus.Validation, (await harness.Sold.AcknowledgeAsync(listing.Id, CancellationToken.None)).Status);
        Assert.Null((await harness.Db.PlatformListings.SingleAsync()).SoldAcknowledgedAt);
    }

    [Fact]
    public async Task A_listing_that_belongs_to_nobody_here_is_not_found()
    {
        Harness harness = NewHarness(nameof(A_listing_that_belongs_to_nobody_here_is_not_found));
        await using HaulScoutDbContext _ = harness.Db;

        Assert.Equal(AppResultStatus.NotFound, (await harness.Sold.AcknowledgeAsync(Guid.NewGuid(), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_sold_listing_nobody_has_linked_to_an_item_is_not_asked_about()
    {
        // Linking it is the imports queue's job; asking twice in two places helps nobody.
        Harness harness = NewHarness(nameof(A_sold_listing_nobody_has_linked_to_an_item_is_not_asked_about));
        await using HaulScoutDbContext _ = harness.Db;
        PlatformListing listing = await harness.Db.PlatformListings.SingleAsync();
        listing.ReconciledInventoryItemId = null;
        listing.ReconcileStatus = PlatformListingReconcileStatus.Pending;
        await harness.Db.SaveChangesAsync();

        Assert.Empty(await harness.Sold.ListAsync(CancellationToken.None));
    }
}
