namespace HaulScout.Application.Inventory;

/// <summary>
/// An uploaded file, described without reference to how it arrived. The photo use case needs a stream and three facts
/// about it; IFormFile is an ASP.NET type, and letting it reach the Application layer would tie inventory to the web
/// framework for no gain. The controller adapts its IFormFile into this.
/// </summary>
/// <param name="Content">The bytes. The caller owns the stream's lifetime.</param>
/// <param name="Length">Declared size, checked against the configured maximum before anything is read.</param>
public sealed record PhotoUpload(Stream Content, string? FileName, string? ContentType, long Length);

/// <summary>A photo after validation, decoding and thumbnailing.</summary>
public sealed record ProcessedPhoto(
    byte[] OriginalBytes,
    byte[] ThumbnailBytes,
    string ContentType,
    string? OriginalFileName,
    long OriginalByteSize,
    long ThumbnailByteSize,
    int Width,
    int Height);

/// <summary>
/// Turning an upload into storable bytes: size and type limits, decoding, and thumbnailing. All of that is mechanism —
/// it depends on an image library and on host configuration — so the use case states what it wants and the
/// implementation decides how.
/// </summary>
public interface IInventoryPhotoProcessor
{
    /// <param name="maxPixels">
    /// The maximum stored width or height; null preserves dimensions. Every image is decoded and re-encoded
    /// without metadata, including uploads that do not need resizing. Listing photos are resized to limit
    /// the space used by galleries synced in bulk.
    /// </param>
    /// <exception cref="InvalidOperationException">The upload is unusable — too large, empty, or not an allowed image
    /// type. The message is written for the operator and is surfaced as a validation failure.</exception>
    Task<ProcessedPhoto> ProcessAsync(PhotoUpload upload, CancellationToken cancellationToken, int? maxPixels = null);

    /// <summary>Removes a photo's backing file, for photos stored on disk rather than in the database.</summary>
    Task DeleteAsync(Guid organizationId, Guid inventoryItemId, string url);
}
