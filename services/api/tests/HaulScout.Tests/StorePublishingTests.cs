using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Storefront;

namespace HaulScout.Tests;

/// <summary>
/// The shelf model: Listed IS the storefront switch. These exercise the real SaveChanges synchronizer (publishing
/// creates the listing, unlisting retires it, photos gate activation) plus the manual-publish gate — the contract
/// the admin and mobile app both rely on.
/// </summary>
public class StorePublishingTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, StoreController Controller, Guid Org) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store)
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        StubCurrentUser user = new(org);
        StorePublishingWorkflowService publishing = new(new EfStoreListingStore(db, TimeProvider.System), user, TimeProvider.System);
        return (db, new StoreController(publishing), org);
    }

    private static InventoryItem ShopReadyItem(string title = "Vintage Brass Lamp") => new()
    {
        Title = title,
        EstimatedSalePrice = 49.99m,
        Status = InventoryStatus.ReadyToList,
        Photos = { NewPhoto() },
    };

    private static InventoryPhoto NewPhoto() => new()
    {
        ContentType = "image/jpeg",
        OriginalBytes = [1],
        ThumbnailBytes = [1],
    };

    private static PublishListingRequest Publish(Guid itemId, decimal price = 49.99m) =>
        new(itemId, price, null, null, null, null, null, false, null, null);

    [Fact]
    public async Task Publishing_marks_the_item_listed_and_the_synchronizer_creates_the_active_listing()
    {
        (HaulScoutDbContext db, StoreController controller, _) = NewContext(nameof(Publishing_marks_the_item_listed_and_the_synchronizer_creates_the_active_listing));
        await using HaulScoutDbContext dbLifetime = db;
        InventoryItem item = ShopReadyItem();
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        ActionResult<StoreListingDto> result = await controller.Publish(Publish(item.Id), CancellationToken.None);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
        StoreListingDto dto = Assert.IsType<StoreListingDto>(created.Value);
        Assert.StartsWith("vintage-brass-lamp-", dto.Slug);   // id-suffixed: unique by construction
        Assert.Equal(49.99m, dto.ListPrice);
        Assert.Equal(StoreListingStatus.Active, dto.Status);
        Assert.Equal(item.Id, dto.InventoryItemId);

        InventoryItem reloaded = await db.InventoryItems.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(InventoryStatus.Listed, reloaded.Status);
        Assert.NotNull(reloaded.ListedAt);
    }

    /// <summary>The listing gate: an operator cannot hand-publish what a shopper couldn't buy.</summary>
    [Fact]
    public async Task Publishing_an_item_without_photos_is_refused_and_says_why()
    {
        (HaulScoutDbContext db, StoreController controller, _) = NewContext(nameof(Publishing_an_item_without_photos_is_refused_and_says_why));
        await using HaulScoutDbContext dbLifetime = db;
        InventoryItem item = new() { Title = "Record Player", EstimatedSalePrice = 120m };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        ActionResult<StoreListingDto> result = await controller.Publish(Publish(item.Id, 120m), CancellationToken.None);

        ProblemDetails refused = ProblemAssert.Invalid(result.Result);
        Assert.Contains("photo", refused.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.StoreListings.AnyAsync(l => l.InventoryItemId == item.Id));
    }

    /// <summary>Republishing is idempotent — one item, one listing, forever.</summary>
    [Fact]
    public async Task Publishing_twice_updates_the_one_listing_instead_of_creating_a_second()
    {
        (HaulScoutDbContext db, StoreController controller, _) = NewContext(nameof(Publishing_twice_updates_the_one_listing_instead_of_creating_a_second));
        await using HaulScoutDbContext dbLifetime = db;
        InventoryItem item = ShopReadyItem("Record Player");
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        Assert.IsType<CreatedAtActionResult>((await controller.Publish(Publish(item.Id, 120m), CancellationToken.None)).Result);
        Assert.IsType<CreatedAtActionResult>((await controller.Publish(Publish(item.Id, 135m), CancellationToken.None)).Result);

        StoreListing listing = Assert.Single(await db.StoreListings.Where(l => l.InventoryItemId == item.Id).ToListAsync());
        Assert.Equal(135m, listing.ListPrice);   // the second publish edited, not duplicated
    }

    [Fact]
    public async Task Unpublishing_returns_the_item_to_ready_and_retires_the_listing()
    {
        (HaulScoutDbContext db, StoreController controller, _) = NewContext(nameof(Unpublishing_returns_the_item_to_ready_and_retires_the_listing));
        await using HaulScoutDbContext dbLifetime = db;
        InventoryItem item = ShopReadyItem();
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        await controller.Publish(Publish(item.Id), CancellationToken.None);
        StoreListing listing = await db.StoreListings.SingleAsync(l => l.InventoryItemId == item.Id);

        ActionResult<StoreListingDto> result = await controller.Unpublish(listing.Id, CancellationToken.None);

        StoreListingDto dto = Assert.IsType<StoreListingDto>(result.Value);
        Assert.Equal(StoreListingStatus.Draft, dto.Status);
        Assert.Equal(InventoryStatus.ReadyToList, (await db.InventoryItems.FirstAsync(i => i.Id == item.Id)).Status);
    }

    /// <summary>
    /// The import path: an item arrives already Listed (a marketplace CSV said so) with no photos. It must NOT be
    /// blocked — but it must land as Draft, invisible to shoppers, and activate itself when its first photo lands.
    /// </summary>
    [Fact]
    public async Task An_imported_listed_item_without_photos_waits_as_draft_and_activates_on_first_photo()
    {
        (HaulScoutDbContext db, StoreController _, Guid org) = NewContext(nameof(An_imported_listed_item_without_photos_waits_as_draft_and_activates_on_first_photo));
        await using HaulScoutDbContext dbLifetime = db;

        // What the eBay import produces: Listed, priced, titled, imageless.
        InventoryItem item = new() { Title = "eBay Import", EstimatedSalePrice = 60m, Status = InventoryStatus.Listed };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        StoreListing listing = await db.StoreListings.SingleAsync(l => l.InventoryItemId == item.Id);
        Assert.Equal(StoreListingStatus.Draft, listing.Status);   // exists for the operator, invisible to shoppers

        db.InventoryPhotos.Add(new InventoryPhoto
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            ContentType = "image/jpeg",
            OriginalBytes = [1],
            ThumbnailBytes = [1],
        });
        await db.SaveChangesAsync();

        Assert.Equal(StoreListingStatus.Active, (await db.StoreListings.SingleAsync(l => l.InventoryItemId == item.Id)).Status);
        Assert.NotNull((await db.StoreListings.SingleAsync(l => l.InventoryItemId == item.Id)).PublishedAt);
    }

    [Fact]
    public async Task Selling_the_item_on_any_channel_marks_its_listing_sold()
    {
        (HaulScoutDbContext db, StoreController controller, _) = NewContext(nameof(Selling_the_item_on_any_channel_marks_its_listing_sold));
        await using HaulScoutDbContext dbLifetime = db;
        InventoryItem item = ShopReadyItem();
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        await controller.Publish(Publish(item.Id), CancellationToken.None);

        item.Status = InventoryStatus.Sold;   // an eBay sale, a manual sale — any path that writes the item
        await db.SaveChangesAsync();

        Assert.Equal(StoreListingStatus.Sold, (await db.StoreListings.SingleAsync(l => l.InventoryItemId == item.Id)).Status);
    }

    /// <summary>
    /// The empty-shop regression: rows that predate the synchronizer have no listings, and a deploy must not
    /// depend on someone remembering a manual reconcile call. The startup sweep builds the shelf on boot.
    /// </summary>
    [Fact]
    public async Task The_startup_sweep_builds_the_shelf_for_items_that_predate_the_synchronizer()
    {
        (HaulScoutDbContext db, StoreController _, Guid org) = NewContext(nameof(The_startup_sweep_builds_the_shelf_for_items_that_predate_the_synchronizer));
        await using HaulScoutDbContext dbLifetime = db;
        InventoryItem item = ShopReadyItem();
        item.Status = InventoryStatus.Listed;
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        // Simulate the pre-synchronizer world: the listing does not exist (out-of-band delete leaves the item
        // untouched, so the change-driven sync has nothing to notice — exactly like rows created before it shipped).
        db.StoreListings.RemoveRange(await db.StoreListings.ToListAsync());
        await db.SaveChangesAsync();
        Assert.False(await db.StoreListings.AnyAsync());

        await StoreShelfBootstrap.ReconcileAllAsync(
            db, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        StoreListing rebuilt = await db.StoreListings.SingleAsync(l => l.InventoryItemId == item.Id);
        Assert.Equal(StoreListingStatus.Active, rebuilt.Status);
    }

    /// <summary>The sweep rebuilds what drifted: rows the synchronizer never saw (it only sees changes).</summary>
    [Fact]
    public async Task The_reconcile_sweep_rebuilds_missing_listings_for_listed_items()
    {
        (HaulScoutDbContext db, StoreController controller, _) = NewContext(nameof(The_reconcile_sweep_rebuilds_missing_listings_for_listed_items));
        await using HaulScoutDbContext dbLifetime = db;
        InventoryItem item = ShopReadyItem();
        item.Status = InventoryStatus.Listed;
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        // Drift: the listing vanishes out-of-band (a raw delete, a bug, a manual edit). The item is untouched in
        // the next save, so the change-driven synchronizer has nothing to notice.
        db.StoreListings.RemoveRange(await db.StoreListings.Where(l => l.InventoryItemId == item.Id).ToListAsync());
        await db.SaveChangesAsync();
        Assert.False(await db.StoreListings.AnyAsync(l => l.InventoryItemId == item.Id));

        ActionResult<ReconcileStoreCatalogResultDto> result = await controller.Reconcile(CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        ReconcileStoreCatalogResultDto dto = Assert.IsType<ReconcileStoreCatalogResultDto>(ok.Value);
        Assert.Equal(1, dto.ListingsCreated);
        Assert.Equal(StoreListingStatus.Active, (await db.StoreListings.SingleAsync(l => l.InventoryItemId == item.Id)).Status);
    }
}
