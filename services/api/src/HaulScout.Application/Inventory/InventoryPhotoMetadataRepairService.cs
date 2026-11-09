using Microsoft.Extensions.Logging;

namespace HaulScout.Application.Inventory;

public sealed record PhotoMetadataRepairPass(int Scanned, int Reencoded, Guid? NextAfter);

public sealed class InventoryPhotoMetadataRepairService(
    IInventoryPhotoBackfillStore store,
    IInventoryPhotoBlobStore blobs,
    IInventoryPhotoProcessor processor,
    ILogger<InventoryPhotoMetadataRepairService> logger)
{
    public async Task<PhotoMetadataRepairPass> RepairAsync(
        Guid? afterPhotoId, int take, bool apply, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, 10);

        var batch = await store.ListForMetadataRepairAsync(afterPhotoId, take, cancellationToken);
        Guid? last = afterPhotoId;
        int reencoded = 0;
        foreach (var photo in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (apply)
            {
                PhotoBytes original = await blobs.ReadOriginalAsync(photo, cancellationToken)
                    ?? throw new InvalidOperationException($"Photo {photo.Id:N} has no readable original.");
                await using MemoryStream input = new(original.Bytes, writable: false);
                ProcessedPhoto processed = await processor.ProcessAsync(
                    new PhotoUpload(input, photo.OriginalFileName, original.ContentType, original.Bytes.LongLength),
                    cancellationToken);

                await blobs.StoreAsync(photo, processed, cancellationToken);
                photo.ContentType = processed.ContentType;
                photo.OriginalByteSize = processed.OriginalByteSize;
                photo.ThumbnailByteSize = processed.ThumbnailByteSize;
                photo.Width = processed.Width;
                photo.Height = processed.Height;
                await store.SaveChangesAsync(cancellationToken);
                reencoded++;
                logger.LogInformation("Photo metadata repair saved. PhotoId={PhotoId}", photo.Id);
            }

            last = photo.Id;
        }

        return new PhotoMetadataRepairPass(batch.Count, reencoded, last);
    }
}
