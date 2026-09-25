using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// Keeps image bytes in a bucket and the key on the photo's row.
///
/// Reading falls back to the row's own columns whenever a key is absent, so photos stored before the bucket existed
/// keep serving while they are moved across in the background. That fallback is the whole reason the move needs
/// neither downtime nor a data migration.
/// </summary>
public sealed class ObjectStoreInventoryPhotoBlobStore(
    IPhotoObjectClient objects,
    IOptions<PhotoObjectStorageOptions> options,
    ILogger<ObjectStoreInventoryPhotoBlobStore> logger) : IInventoryPhotoBlobStore
{
    private readonly PhotoObjectStorageOptions settings = options.Value;

    public async Task StoreAsync(InventoryPhoto photo, ProcessedPhoto processed, CancellationToken cancellationToken)
    {
        string originalKey = KeyFor(photo, $"original{ExtensionFor(processed.ContentType)}");
        string thumbnailKey = KeyFor(photo, "thumbnail.jpg");

        await objects.PutAsync(originalKey, processed.OriginalBytes, processed.ContentType, cancellationToken);
        await objects.PutAsync(thumbnailKey, processed.ThumbnailBytes, InventoryPhotoFormats.ThumbnailContentType, cancellationToken);

        photo.OriginalObjectKey = originalKey;
        photo.ThumbnailObjectKey = thumbnailKey;

        // The bucket holds the images now. Leaving a copy in the row would defeat the point of moving them.
        photo.OriginalBytes = null;
        photo.ThumbnailBytes = null;
    }

    public Task<PhotoBytes?> ReadOriginalAsync(InventoryPhoto photo, CancellationToken cancellationToken) =>
        ReadAsync(photo.OriginalObjectKey, photo.OriginalBytes, photo.ContentType, cancellationToken);

    public Task<PhotoBytes?> ReadThumbnailAsync(InventoryPhoto photo, CancellationToken cancellationToken) =>
        ReadAsync(photo.ThumbnailObjectKey, photo.ThumbnailBytes, InventoryPhotoFormats.ThumbnailContentType, cancellationToken);

    public async Task ForgetAsync(InventoryPhoto photo, CancellationToken cancellationToken)
    {
        foreach (string key in new[] { photo.OriginalObjectKey, photo.ThumbnailObjectKey }.OfType<string>())
        {
            try
            {
                await objects.DeleteAsync(key, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // An object nobody can reach any more costs a fraction of a cent. A delete the operator cannot
                // complete costs a great deal more, so the deletion goes ahead and the orphan is written down.
                logger.LogWarning(exception, "Photo image could not be deleted from object storage. Key={Key}", key);
            }
        }
    }

    private async Task<PhotoBytes?> ReadAsync(string? key, byte[]? held, string contentType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(key))
        {
            return Held(held, contentType);
        }

        if (await objects.GetAsync(key, cancellationToken) is byte[] bytes)
        {
            return new PhotoBytes(bytes, contentType);
        }

        // The row says the image is in the bucket and it is not. Serving the copy the row still holds, when it has
        // one, beats serving a hole — and either way this is worth an operator's attention.
        logger.LogError("Photo image is missing from object storage. Key={Key}", key);
        return Held(held, contentType);
    }

    private static PhotoBytes? Held(byte[]? bytes, string contentType) =>
        bytes is { Length: > 0 } ? new PhotoBytes(bytes, contentType) : null;

    /// <summary>
    /// Keys carry the organization and then the photo, so a bucket can be read by a person: everything one
    /// organization owns sits under one path, and a photo's two images sit together under its id.
    /// </summary>
    private string KeyFor(InventoryPhoto photo, string file) =>
        $"{settings.NormalizedPrefix}{photo.OrganizationId:N}/{photo.Id:N}/{file}";

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg",
    };
}
