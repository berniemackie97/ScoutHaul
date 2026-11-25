using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Inventory;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Photos following a marketplace listing onto the item it is linked to. What matters here is not that a photo can
/// be fetched, but that an item's photos survive the process: the ones that did not change keep their identity —
/// and so the addresses the storefront serves them at — and an item is never emptied because a marketplace was
/// unreachable.
/// </summary>
public sealed class ListingPhotoSyncTests
{
    private const string First = "https://images1.vinted.net/tc/a/1.webp";
    private const string Second = "https://images1.vinted.net/tc/b/1.webp";
    private const string Third = "https://images1.vinted.net/tc/c/1.webp";

    /// <summary>A marketplace's image host, answering with a one-pixel image — or refusing, when asked to.</summary>
    private sealed class FakeImages : IRemoteImageFetcher
    {
        public HashSet<string> Unreachable { get; } = [];
        public List<string> Fetched { get; } = [];

        public Task<PhotoUpload> FetchAsync(string url, CancellationToken cancellationToken)
        {
            if (Unreachable.Contains(url))
            {
                throw new RemoteImageFetchException("That image could not be downloaded (503).");
            }

            Fetched.Add(url);
            return Task.FromResult(new PhotoUpload(new MemoryStream([1, 2, 3]), "photo.webp", "image/webp", 3));
        }
    }

    private sealed class FakeProcessor : IInventoryPhotoProcessor
    {
        public List<int?> Ceilings { get; } = [];

        public async Task<ProcessedPhoto> ProcessAsync(PhotoUpload upload, CancellationToken cancellationToken, int? maxPixels = null)
        {
            Ceilings.Add(maxPixels);
            await using MemoryStream buffer = new();
            await upload.Content.CopyToAsync(buffer, cancellationToken);
            byte[] bytes = buffer.ToArray();
            return new ProcessedPhoto(bytes, [9], "image/jpeg", upload.FileName, bytes.LongLength, 1, 1200, 1600);
        }

        public Task DeleteAsync(Guid organizationId, Guid inventoryItemId, string url) => Task.CompletedTask;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required HaulScoutDbContext Db { get; init; }
        public required Guid Org { get; init; }
        public required FakeImages Images { get; init; }
        public required FakeProcessor Processor { get; init; }
        public required InventoryPhotoSyncService Sync { get; init; }
        public required ChannelAccount Account { get; init; }

        public InventoryItem Item(string title = "Plaid shirt")
        {
            InventoryItem item = new() { OrganizationId = Org, Title = title };
            Db.InventoryItems.Add(item);
            return item;
        }

        public PlatformListing Listing(
            InventoryItem item,
            IEnumerable<string> photos,
            string externalId = "9482228763",
            ChannelAccount? account = null,
            string platform = "Vinted")
        {
            List<string> addresses = [.. photos];
            PlatformListing listing = new()
            {
                OrganizationId = Org,
                Platform = platform,
                ExternalListingId = externalId,
                Title = item.Title,
                ChannelAccountId = (account ?? Account).Id,
                ReconcileStatus = PlatformListingReconcileStatus.Linked,
                ReconciledInventoryItemId = item.Id,
                PhotoUrlsJson = System.Text.Json.JsonSerializer.Serialize(addresses),
                PhotoUrlsKey = ListingPhotoRules.KeyOf(addresses),
                PhotoUrlsChangedAt = DateTimeOffset.UtcNow,
            };
            Db.PlatformListings.Add(listing);
            return listing;
        }

        public Task<InventoryItem> Reload(Guid id) =>
            Db.InventoryItems.Include(item => item.Photos).SingleAsync(item => item.Id == id);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static Harness NewHarness(bool photoSyncEnabled = true)
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(
            new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant);
        ChannelAccount account = new() { OrganizationId = org, Channel = "Vinted", DisplayName = "Overhaul Vinted" };
        db.ChannelAccounts.Add(account);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul", MarketplacePhotoSyncEnabled = photoSyncEnabled });
        db.SaveChanges();

        FakeImages images = new();
        FakeProcessor processor = new();
        return new Harness
        {
            Db = db,
            Org = org,
            Images = images,
            Processor = processor,
            Account = account,
            Sync = new InventoryPhotoSyncService(new EfInventoryPhotoSyncStore(db), images, processor, new DatabaseInventoryPhotoBlobStore(), TimeProvider.System),
        };
    }

    [Fact]
    public async Task A_listings_photos_become_the_items_photos_in_the_listings_order()
    {
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        PlatformListing listing = harness.Listing(item, [First, Second]);
        await harness.Db.SaveChangesAsync();

        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal((1, 1, 2, 0), (outcome.Listings, outcome.ItemsUpdated, outcome.PhotosAdded, outcome.PhotosRemoved));
        Assert.Empty(outcome.Failures);

        InventoryItem synced = await harness.Reload(item.Id);
        Assert.Equal([First, Second], synced.Photos.OrderBy(photo => photo.SortOrder).Select(photo => photo.SourceUrl));
        InventoryPhoto hero = Assert.Single(synced.Photos, photo => photo.IsHero);
        Assert.Equal(First, hero.SourceUrl);
        Assert.Contains(hero.Id.ToString(), synced.PhotoLocalReferencesJson);
        Assert.Equal(ListingPhotoRules.SourceKey("Vinted", "9482228763", listing.PhotoUrlsKey!), synced.PhotoSourceKey);

        // Kept at a size a page shows rather than whatever the marketplace serves.
        Assert.All(harness.Processor.Ceilings, ceiling => Assert.Equal(ListingPhotoRules.MaxPixels, ceiling));

        // Nothing is owed a second time.
        Assert.Empty((await harness.Sync.SyncPendingAsync(10, CancellationToken.None)).Failures);
        Assert.Equal(2, harness.Images.Fetched.Count);
    }

    [Fact]
    public async Task A_photo_the_item_already_holds_keeps_its_identity_when_the_listing_changes()
    {
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        PlatformListing listing = harness.Listing(item, [First, Second]);
        await harness.Db.SaveChangesAsync();
        await harness.Sync.SyncPendingAsync(10, CancellationToken.None);
        Guid keptId = (await harness.Reload(item.Id)).Photos.Single(photo => photo.SourceUrl == First).Id;

        // The seller swapped the second photo and added a third.
        List<string> now = [First, Third];
        listing.PhotoUrlsJson = System.Text.Json.JsonSerializer.Serialize(now);
        listing.PhotoUrlsKey = ListingPhotoRules.KeyOf(now);
        await harness.Db.SaveChangesAsync();

        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal((1, 1, 1), (outcome.ItemsUpdated, outcome.PhotosAdded, outcome.PhotosRemoved));
        InventoryItem synced = await harness.Reload(item.Id);
        // The photo that did not change is the same row, so the address the storefront serves it at still works.
        Assert.Equal(keptId, synced.Photos.Single(photo => photo.SourceUrl == First).Id);
        Assert.Equal([First, Third], synced.Photos.OrderBy(photo => photo.SortOrder).Select(photo => photo.SourceUrl));
        Assert.Equal(3, harness.Images.Fetched.Count);
    }

    [Fact]
    public async Task The_listings_photos_replace_photos_someone_uploaded_here()
    {
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        harness.Db.InventoryPhotos.Add(new InventoryPhoto
        {
            OrganizationId = harness.Org,
            InventoryItemId = item.Id,
            IsHero = true,
            ContentType = "image/jpeg",
            OriginalBytes = [7],
            ThumbnailBytes = [7],
        });
        harness.Listing(item, [First]);
        await harness.Db.SaveChangesAsync();

        await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        InventoryItem synced = await harness.Reload(item.Id);
        InventoryPhoto only = Assert.Single(synced.Photos);
        Assert.Equal(First, only.SourceUrl);
        Assert.True(only.IsHero);
    }

    [Fact]
    public async Task An_item_is_never_emptied_because_the_marketplace_would_not_answer()
    {
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        PlatformListing listing = harness.Listing(item, [First, Second]);
        await harness.Db.SaveChangesAsync();
        await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        // The seller replaced every photo, and the marketplace's images are down.
        List<string> now = [Third];
        listing.PhotoUrlsJson = System.Text.Json.JsonSerializer.Serialize(now);
        listing.PhotoUrlsKey = ListingPhotoRules.KeyOf(now);
        harness.Images.Unreachable.Add(Third);
        await harness.Db.SaveChangesAsync();

        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal(0, outcome.ItemsUpdated);
        Assert.Contains("none of its 1 photos could be fetched", Assert.Single(outcome.Failures));
        InventoryItem kept = await harness.Reload(item.Id);
        Assert.Equal([First, Second], kept.Photos.OrderBy(photo => photo.SortOrder).Select(photo => photo.SourceUrl));
        Assert.Contains("none of its 1 photos could be fetched", (await harness.Db.PlatformListings.SingleAsync()).PhotoSyncError);
    }

    [Fact]
    public async Task A_photo_that_failed_is_fetched_again_after_the_wait_and_only_that_one()
    {
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        PlatformListing listing = harness.Listing(item, [First, Second]);
        harness.Images.Unreachable.Add(Second);
        await harness.Db.SaveChangesAsync();

        Assert.Contains("1 of 2 photos could not be fetched", Assert.Single((await harness.Sync.SyncPendingAsync(10, CancellationToken.None)).Failures));

        // Still inside the wait: nothing is tried again.
        harness.Images.Fetched.Clear();
        Assert.Equal(0, (await harness.Sync.SyncPendingAsync(10, CancellationToken.None)).Listings);

        // After the wait, with the host back, only the missing photo is fetched and the listing is done.
        harness.Images.Unreachable.Clear();
        listing.PhotosSyncedAt = DateTimeOffset.UtcNow.AddHours(-2);
        await harness.Db.SaveChangesAsync();
        PhotoSyncOutcome retry = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal([Second], harness.Images.Fetched);
        Assert.Empty(retry.Failures);
        InventoryItem whole = await harness.Reload(item.Id);
        Assert.Equal([First, Second], whole.Photos.OrderBy(photo => photo.SortOrder).Select(photo => photo.SourceUrl));
        PlatformListing done = await harness.Db.PlatformListings.SingleAsync();
        Assert.Equal(done.PhotoUrlsKey, done.PhotoSyncedKey);
        Assert.Null(done.PhotoSyncError);
        Assert.Equal(0, (await harness.Sync.SyncPendingAsync(10, CancellationToken.None)).Listings);
    }

    [Fact]
    public async Task Only_the_marketplaces_own_image_addresses_are_followed()
    {
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        harness.Listing(item, ["https://images1.vinted.net.evil.example/a.webp", "http://images1.vinted.net/a.webp", First]);
        await harness.Db.SaveChangesAsync();

        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal([First], harness.Images.Fetched);
        Assert.Equal(1, outcome.PhotosAdded);
        Assert.Contains("2 of 3 photos could not be fetched", Assert.Single(outcome.Failures));
    }

    [Fact]
    public async Task An_item_listed_twice_takes_its_photos_from_the_listing_showing_the_most()
    {
        await using Harness harness = NewHarness();
        ChannelAccount ebay = new() { OrganizationId = harness.Org, Channel = "eBay", DisplayName = "Overhaul eBay" };
        harness.Db.ChannelAccounts.Add(ebay);
        InventoryItem item = harness.Item();
        harness.Listing(item, [First, Second], "vinted-1");
        PlatformListing thin = harness.Listing(item, ["https://i.ebayimg.com/images/g/x/s-l1600.jpg"], "ebay-1", ebay);
        thin.Platform = "eBay";
        await harness.Db.SaveChangesAsync();

        await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        InventoryItem synced = await harness.Reload(item.Id);
        Assert.Equal([First, Second], synced.Photos.OrderBy(photo => photo.SortOrder).Select(photo => photo.SourceUrl));
        // The listing that stood aside is not asked again until its own photos change.
        Assert.All(await harness.Db.PlatformListings.ToListAsync(), listing => Assert.Equal(listing.PhotoUrlsKey, listing.PhotoSyncedKey));
    }

    [Fact]
    public async Task Photos_follow_only_the_accounts_that_asked_for_it()
    {
        await using Harness harness = NewHarness(photoSyncEnabled: false);
        InventoryItem item = harness.Item();
        harness.Listing(item, [First]);
        await harness.Db.SaveChangesAsync();

        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal(0, outcome.Listings);
        Assert.Empty(harness.Images.Fetched);
        Assert.Empty((await harness.Reload(item.Id)).Photos);
    }

    [Fact]
    public async Task A_listing_with_no_item_or_no_photos_is_not_work()
    {
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        PlatformListing unlinked = harness.Listing(item, [First], "unlinked");
        unlinked.ReconciledInventoryItemId = null;
        unlinked.ReconcileStatus = PlatformListingReconcileStatus.Pending;
        harness.Listing(item, [], "empty");
        await harness.Db.SaveChangesAsync();

        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Empty(harness.Images.Fetched);
        Assert.Equal(0, outcome.PhotosAdded);
    }

    private const string EbayPhoto = "https://i.ebayimg.com/images/g/one/s-l1600.jpg";

    [Fact]
    public async Task A_marketplace_showing_fewer_photos_cannot_take_an_item_over()
    {
        // eBay's grid carries one picture per listing. It must never replace a Vinted gallery with that one picture.
        await using Harness harness = NewHarness();
        ChannelAccount ebay = new() { OrganizationId = harness.Org, Channel = "eBay", DisplayName = "Overhaul eBay" };
        harness.Db.ChannelAccounts.Add(ebay);
        InventoryItem item = harness.Item();
        harness.Listing(item, [First, Second, Third]);
        await harness.Db.SaveChangesAsync();
        await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        harness.Listing(item, [EbayPhoto], "377296454991", ebay, "eBay");
        await harness.Db.SaveChangesAsync();
        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal(0, outcome.PhotosRemoved);
        InventoryItem synced = await harness.Reload(item.Id);
        Assert.Equal([First, Second, Third], synced.Photos.OrderBy(photo => photo.SortOrder).Select(photo => photo.SourceUrl));
    }

    [Fact]
    public async Task An_item_keeps_its_photos_when_the_only_listing_left_shows_fewer()
    {
        // The richer listing ended. Its photos are still the best pictures of the item, so they stay.
        await using Harness harness = NewHarness();
        ChannelAccount ebay = new() { OrganizationId = harness.Org, Channel = "eBay", DisplayName = "Overhaul eBay" };
        harness.Db.ChannelAccounts.Add(ebay);
        InventoryItem item = harness.Item();
        PlatformListing vinted = harness.Listing(item, [First, Second, Third]);
        await harness.Db.SaveChangesAsync();
        await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        harness.Db.PlatformListings.Remove(vinted);
        harness.Listing(item, [EbayPhoto], "377296454991", ebay, "eBay");
        await harness.Db.SaveChangesAsync();
        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal(0, outcome.PhotosRemoved);
        Assert.Equal(3, (await harness.Reload(item.Id)).Photos.Count);
    }

    [Fact]
    public async Task The_listing_an_items_photos_came_from_may_still_take_one_away()
    {
        // A seller who removes a picture from their own listing means it: that one goes.
        await using Harness harness = NewHarness();
        InventoryItem item = harness.Item();
        PlatformListing listing = harness.Listing(item, [First, Second, Third]);
        await harness.Db.SaveChangesAsync();
        await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        listing.PhotoUrlsJson = System.Text.Json.JsonSerializer.Serialize(new[] { First, Second });
        listing.PhotoUrlsKey = ListingPhotoRules.KeyOf([First, Second]);
        listing.PhotoUrlsChangedAt = DateTimeOffset.UtcNow;
        await harness.Db.SaveChangesAsync();
        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, outcome.PhotosRemoved);
        Assert.Equal([First, Second], (await harness.Reload(item.Id)).Photos.OrderBy(photo => photo.SortOrder).Select(photo => photo.SourceUrl));
    }

    [Fact]
    public async Task Photos_follow_listings_for_the_whole_business_or_not_at_all()
    {
        await using Harness harness = NewHarness(photoSyncEnabled: false);
        InventoryItem item = harness.Item();
        harness.Listing(item, [First, Second]);
        await harness.Db.SaveChangesAsync();

        PhotoSyncOutcome outcome = await harness.Sync.SyncPendingAsync(10, CancellationToken.None);

        Assert.Equal(0, outcome.Listings);
        Assert.Empty((await harness.Reload(item.Id)).Photos);
    }
}
