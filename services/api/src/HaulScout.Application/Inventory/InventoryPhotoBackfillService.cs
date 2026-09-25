using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>What one pass of the move did, and what is left.</summary>
/// <param name="Moved">Photos whose images now live in object storage.</param>
/// <param name="Remaining">Photos still holding their images in the database once this pass had finished.</param>
public sealed record PhotoBackfillPass(int Moved, int Remaining);

/// <summary>
/// Moves photos stored before object storage was configured into it, a batch at a time.
///
/// It exists so that turning object storage on is a setting rather than a migration: reads already fall back to the
/// columns, so photos serve normally throughout, and this works through the backlog in the background.
///
/// A photo is moved by handing its existing images back to <see cref="IInventoryPhotoBlobStore.StoreAsync"/> — the
/// same call a fresh upload makes — so there is one description of how a photo is stored, not two. That also makes
/// the move safe to interrupt: a photo whose upload succeeded but whose row was never saved is picked up again and
/// written to the same key.
/// </summary>
public sealed class InventoryPhotoBackfillService(IInventoryPhotoBackfillStore store, IInventoryPhotoBlobStore blobs)
{
    public async Task<PhotoBackfillPass> MoveAsync(int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<InventoryPhoto> held = await store.ListHeldInDatabaseAsync(take, cancellationToken);
        int moved = 0;

        foreach (InventoryPhoto photo in held)
        {
            if (photo.OriginalBytes is not { Length: > 0 } original || photo.ThumbnailBytes is not { Length: > 0 } thumbnail)
            {
                // Nothing to move. Rows like this are not expected, and skipping beats writing an empty object.
                continue;
            }

            await blobs.StoreAsync(
                photo,
                new ProcessedPhoto(
                    original,
                    thumbnail,
                    photo.ContentType,
                    photo.OriginalFileName,
                    photo.OriginalByteSize,
                    photo.ThumbnailByteSize,
                    photo.Width ?? 0,
                    photo.Height ?? 0),
                cancellationToken);
            moved++;
        }

        if (moved > 0)
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return new PhotoBackfillPass(moved, await store.CountHeldInDatabaseAsync(cancellationToken));
    }
}
