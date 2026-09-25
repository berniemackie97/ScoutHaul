using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Facts about the shape photos are stored in, shared by everything that reads them back. The processor re-encodes
/// every thumbnail as JPEG whatever the original was, so a thumbnail's type is known without consulting the row.
/// </summary>
public static class InventoryPhotoFormats
{
    public const string ThumbnailContentType = "image/jpeg";
}

/// <summary>An image ready to be written to a response, however it was stored.</summary>
public sealed record PhotoBytes(byte[] Bytes, string ContentType);

/// <summary>
/// Where a photo's image bytes are kept. The use cases above this port decide which photos exist, in what order and
/// who may see them; none of them needs to know whether the bytes sit in a database column or in a bucket.
///
/// Two implementations exist. One keeps bytes in the photo's own columns, which is all a single small deployment
/// needs. The other writes them to object storage and records the key, because image bytes dominate this schema —
/// they are the one kind of row that grows without limit as listings are synced, and keeping them in Postgres makes
/// every backup, restore and write-ahead log record carry the whole gallery.
///
/// Reading always falls back to the columns, so photos stored before object storage was configured keep working and
/// the move can be done in the background rather than in a migration.
/// </summary>
public interface IInventoryPhotoBlobStore
{
    /// <summary>
    /// Puts a freshly processed image behind <paramref name="photo"/>, setting whichever of the byte columns or the
    /// object keys this implementation uses. The caller saves the photo afterwards.
    /// </summary>
    Task StoreAsync(InventoryPhoto photo, ProcessedPhoto processed, CancellationToken cancellationToken);

    /// <summary>The full-size image, or null when it cannot be found at all.</summary>
    Task<PhotoBytes?> ReadOriginalAsync(InventoryPhoto photo, CancellationToken cancellationToken);

    /// <summary>The thumbnail, or null when it cannot be found at all.</summary>
    Task<PhotoBytes?> ReadThumbnailAsync(InventoryPhoto photo, CancellationToken cancellationToken);

    /// <summary>
    /// Releases whatever this implementation holds for a photo being deleted. Failing to reach storage must not stop
    /// a deletion: a stranded object costs a fraction of a cent, a photo the operator cannot delete costs their trust.
    /// </summary>
    Task ForgetAsync(InventoryPhoto photo, CancellationToken cancellationToken);
}
