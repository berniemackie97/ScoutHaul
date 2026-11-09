using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Tests;

public sealed class PhotoMetadataRepairTests
{
    private sealed class Store(params InventoryPhoto[] photos) : IInventoryPhotoBackfillStore
    {
        public int Saves { get; private set; }

        public Task<IReadOnlyList<InventoryPhoto>> ListForMetadataRepairAsync(Guid? after, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryPhoto>>(photos
                .Where(photo => after is null || photo.Id.CompareTo(after.Value) > 0)
                .OrderBy(photo => photo.Id)
                .Take(take)
                .ToArray());

        public Task<IReadOnlyList<InventoryPhoto>> ListHeldInDatabaseAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryPhoto>>([]);

        public Task<int> CountHeldInDatabaseAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class Processor : IInventoryPhotoProcessor
    {
        public int Calls { get; private set; }

        public Task<ProcessedPhoto> ProcessAsync(PhotoUpload upload, CancellationToken cancellationToken, int? maxPixels = null)
        {
            Calls++;
            return Task.FromResult(new ProcessedPhoto(
                [9, 8, 7], [6, 5], "image/jpeg", upload.FileName, 3, 2, 12, 8));
        }

        public Task DeleteAsync(Guid organizationId, Guid inventoryItemId, string url) => Task.CompletedTask;
    }

    private static InventoryPhoto Photo(byte[]? original = null) => new()
    {
        OrganizationId = Guid.NewGuid(),
        InventoryItemId = Guid.NewGuid(),
        ContentType = "image/jpeg",
        OriginalBytes = original ?? new byte[] { 1, 2, 3 },
        ThumbnailBytes = [4, 5],
        OriginalByteSize = 3,
        ThumbnailByteSize = 2,
        Width = 1600,
        Height = 1200,
    };

    private static InventoryPhotoMetadataRepairService NewRepair(Store store, IInventoryPhotoBlobStore blobs, Processor processor) =>
        new(store, blobs, processor, NullLogger<InventoryPhotoMetadataRepairService>.Instance);

    [Fact]
    public async Task DryRunInspectsIdsWithoutReadingOrModifyingPhotos()
    {
        InventoryPhoto photo = Photo();
        Store store = new(photo);
        Processor processor = new();
        PhotoMetadataRepairPass result = await NewRepair(store, new DatabaseInventoryPhotoBlobStore(), processor)
            .RepairAsync(null, 10, false, CancellationToken.None);

        Assert.Equal(1, result.Scanned);
        Assert.Equal(0, result.Reencoded);
        Assert.Equal(photo.Id, result.NextAfter);
        Assert.Equal(0, store.Saves);
        Assert.Equal(0, processor.Calls);
        Assert.Equal(new byte[] { 1, 2, 3 }, photo.OriginalBytes);
    }

    [Fact]
    public async Task OversizedMaintenanceBatchIsRejectedBeforeReading()
    {
        Store store = new(Photo());
        Processor processor = new();
        var repair = NewRepair(store, new DatabaseInventoryPhotoBlobStore(), processor);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repair.RepairAsync(null, 11, true, CancellationToken.None));
        Assert.Equal(0, processor.Calls);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task ApplyStoresReencodedOriginalAndThumbnailAndUpdatesDimensions()
    {
        InventoryPhoto photo = Photo();
        Store store = new(photo);
        Processor processor = new();
        PhotoMetadataRepairPass result = await NewRepair(store, new DatabaseInventoryPhotoBlobStore(), processor)
            .RepairAsync(null, 1, true, CancellationToken.None);

        Assert.Equal(1, result.Reencoded);
        Assert.Equal(photo.Id, result.NextAfter);
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, processor.Calls);
        Assert.Equal(new byte[] { 9, 8, 7 }, photo.OriginalBytes);
        Assert.Equal(new byte[] { 6, 5 }, photo.ThumbnailBytes);
        Assert.Equal(12, photo.Width);
        Assert.Equal(8, photo.Height);
    }

    [Fact]
    public async Task CursorResumesAfterCommittedPhotoWithoutReencodingIt()
    {
        InventoryPhoto[] photos = [Photo(), Photo(), Photo()];
        Store store = new(photos);
        Processor processor = new();
        var repair = NewRepair(store, new DatabaseInventoryPhotoBlobStore(), processor);
        var first = await repair.RepairAsync(null, 1, true, CancellationToken.None);
        var second = await repair.RepairAsync(first.NextAfter, 1, true, CancellationToken.None);
        Assert.Equal(2, store.Saves);
        Assert.Equal(2, processor.Calls);
        Assert.NotEqual(first.NextAfter, second.NextAfter);
        Assert.Equal(1, second.Reencoded);
    }

    [Fact]
    public async Task MissingOriginalStopsWithoutAdvancingToLaterPhotos()
    {
        InventoryPhoto first = Photo();
        InventoryPhoto missing = Photo();
        first.Id = Guid.Parse("11111111-1111-4111-8111-111111111111");
        missing.Id = Guid.Parse("22222222-2222-4222-8222-222222222222");
        missing.OriginalBytes = null;
        Store store = new(first, missing);
        Processor processor = new();
        var repair = NewRepair(store, new DatabaseInventoryPhotoBlobStore(), processor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repair.RepairAsync(null, 2, true, CancellationToken.None));
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, processor.Calls);
        Assert.Equal(new byte[] { 9, 8, 7 }, first.OriginalBytes);
        Assert.Null(missing.OriginalBytes);
    }

    [Fact]
    public async Task ObjectStoreRewritesPhotoAtExistingKeysWithoutChangingIdentity()
    {
        InventoryPhoto photo = Photo();
        string prefix = $"{photo.OrganizationId:N}/{photo.Id:N}";
        string originalKey = $"{prefix}/original.jpg";
        string thumbnailKey = $"{prefix}/thumbnail.jpg";
        photo.OriginalBytes = null;
        photo.ThumbnailBytes = null;
        photo.OriginalObjectKey = originalKey;
        photo.ThumbnailObjectKey = thumbnailKey;
        FakePhotoObjects objects = new();
        await objects.PutAsync(originalKey, [1, 2, 3], "image/jpeg", CancellationToken.None);
        await objects.PutAsync(thumbnailKey, [4, 5], "image/jpeg", CancellationToken.None);
        Store store = new(photo);
        var blobs = new ObjectStoreInventoryPhotoBlobStore(objects,
            Options.Create(new PhotoObjectStorageOptions { Bucket = "photos" }),
            NullLogger<ObjectStoreInventoryPhotoBlobStore>.Instance);

        await NewRepair(store, blobs, new Processor()).RepairAsync(null, 1, true, CancellationToken.None);
        Assert.Equal(originalKey, photo.OriginalObjectKey);
        Assert.Equal(thumbnailKey, photo.ThumbnailObjectKey);
        Assert.Equal(new byte[] { 9, 8, 7 }, objects.Stored[originalKey].Bytes);
        Assert.Equal(new byte[] { 6, 5 }, objects.Stored[thumbnailKey].Bytes);
        Assert.Equal(1, store.Saves);
    }
}
