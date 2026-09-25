using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Tests;

/// <summary>
/// Moving photos that were stored before object storage was configured. The move has to be safe to interrupt and
/// safe to repeat, because it runs in the background of a live service: a restart halfway through must leave every
/// photo either where it was or where it is going, never nowhere.
/// </summary>
public sealed class PhotoBackfillTests
{
    /// <summary>The photos owed to the bucket, in a list. Saving is a no-op: nothing here is about persistence.</summary>
    private sealed class FakeBackfillStore(params InventoryPhoto[] photos) : IInventoryPhotoBackfillStore
    {
        public int Saves { get; private set; }

        public Task<IReadOnlyList<InventoryPhoto>> ListHeldInDatabaseAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryPhoto>>(Held().Take(take).ToList());

        public Task<int> CountHeldInDatabaseAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Held().Count());

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }

        private IEnumerable<InventoryPhoto> Held() =>
            photos.Where(photo => photo.OriginalObjectKey is null && photo.OriginalBytes is not null);
    }

    private static InventoryPhoto HeldPhoto() => new()
    {
        OrganizationId = Guid.NewGuid(),
        InventoryItemId = Guid.NewGuid(),
        ContentType = "image/jpeg",
        OriginalBytes = [1, 2, 3],
        ThumbnailBytes = [4, 5],
        OriginalByteSize = 3,
        ThumbnailByteSize = 2,
        Width = 1600,
        Height = 1200,
    };

    private static InventoryPhotoBackfillService NewBackfill(FakePhotoObjects objects, IInventoryPhotoBackfillStore store) =>
        new(store,
            new ObjectStoreInventoryPhotoBlobStore(
                objects,
                Options.Create(new PhotoObjectStorageOptions { Bucket = "photos" }),
                NullLogger<ObjectStoreInventoryPhotoBlobStore>.Instance));

    [Fact]
    public async Task A_pass_moves_a_batch_and_says_what_is_left()
    {
        InventoryPhoto[] photos = [HeldPhoto(), HeldPhoto(), HeldPhoto()];
        FakePhotoObjects objects = new();
        FakeBackfillStore store = new(photos);

        PhotoBackfillPass pass = await NewBackfill(objects, store).MoveAsync(2, CancellationToken.None);

        Assert.Equal(2, pass.Moved);
        Assert.Equal(1, pass.Remaining);
        Assert.Equal(1, store.Saves);
        Assert.Equal(4, objects.Stored.Count); // two images each
        Assert.All(photos.Take(2), photo =>
        {
            Assert.NotNull(photo.OriginalObjectKey);
            Assert.Null(photo.OriginalBytes);
        });
    }

    [Fact]
    public async Task Passes_continue_until_nothing_is_left()
    {
        InventoryPhoto[] photos = [HeldPhoto(), HeldPhoto(), HeldPhoto()];
        FakePhotoObjects objects = new();
        FakeBackfillStore store = new(photos);
        InventoryPhotoBackfillService backfill = NewBackfill(objects, store);

        await backfill.MoveAsync(2, CancellationToken.None);
        PhotoBackfillPass last = await backfill.MoveAsync(2, CancellationToken.None);

        Assert.Equal(1, last.Moved);
        Assert.Equal(0, last.Remaining);
        Assert.All(photos, photo => Assert.Null(photo.OriginalBytes));
    }

    [Fact]
    public async Task Moving_the_same_photo_twice_writes_the_same_objects_rather_than_duplicates()
    {
        // A pass whose upload succeeded but whose save never landed is picked up again. It must not leave litter.
        InventoryPhoto photo = HeldPhoto();
        FakePhotoObjects objects = new();
        InventoryPhotoBackfillService backfill = NewBackfill(objects, new FakeBackfillStore(photo));

        await backfill.MoveAsync(10, CancellationToken.None);
        string? key = photo.OriginalObjectKey;
        photo.OriginalObjectKey = null;
        photo.OriginalBytes = [1, 2, 3];
        photo.ThumbnailBytes = [4, 5];
        await backfill.MoveAsync(10, CancellationToken.None);

        Assert.Equal(key, photo.OriginalObjectKey);
        Assert.Equal(2, objects.Stored.Count);
        Assert.Equal(4, objects.Puts);
    }

    [Fact]
    public async Task A_photo_with_no_image_at_all_is_left_alone()
    {
        InventoryPhoto empty = HeldPhoto();
        empty.ThumbnailBytes = null;
        FakePhotoObjects objects = new();

        PhotoBackfillPass pass = await NewBackfill(objects, new FakeBackfillStore(empty)).MoveAsync(10, CancellationToken.None);

        Assert.Equal(0, pass.Moved);
        Assert.Empty(objects.Stored);
        Assert.Null(empty.OriginalObjectKey);
    }
}
