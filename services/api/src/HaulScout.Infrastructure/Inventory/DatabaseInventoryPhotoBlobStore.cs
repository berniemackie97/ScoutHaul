using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// Keeps image bytes in the photo's own columns. This is the default, and it is all a deployment needs until the
/// gallery grows: no bucket to provision, no credentials to hold, and a photo is saved and deleted in the same
/// transaction as the row that describes it.
///
/// It stops being the right answer once listings are synced in bulk, because the bytes then dominate the database.
/// <see cref="ObjectStoreInventoryPhotoBlobStore"/> takes over at that point.
/// </summary>
public sealed class DatabaseInventoryPhotoBlobStore : IInventoryPhotoBlobStore
{
    public Task StoreAsync(InventoryPhoto photo, ProcessedPhoto processed, CancellationToken cancellationToken)
    {
        photo.OriginalBytes = processed.OriginalBytes;
        photo.ThumbnailBytes = processed.ThumbnailBytes;
        photo.OriginalObjectKey = null;
        photo.ThumbnailObjectKey = null;
        return Task.CompletedTask;
    }

    public Task<PhotoBytes?> ReadOriginalAsync(InventoryPhoto photo, CancellationToken cancellationToken) =>
        Task.FromResult(Read(photo.OriginalBytes, photo.ContentType));

    public Task<PhotoBytes?> ReadThumbnailAsync(InventoryPhoto photo, CancellationToken cancellationToken) =>
        Task.FromResult(Read(photo.ThumbnailBytes, InventoryPhotoFormats.ThumbnailContentType));

    public Task ForgetAsync(InventoryPhoto photo, CancellationToken cancellationToken) => Task.CompletedTask;

    private static PhotoBytes? Read(byte[]? bytes, string contentType) =>
        bytes is { Length: > 0 } ? new PhotoBytes(bytes, contentType) : null;
}
