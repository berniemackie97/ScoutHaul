using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <param name="Listings">Listings looked at.</param>
/// <param name="ItemsUpdated">Items whose photos changed.</param>
/// <param name="PhotosAdded">Photos fetched and kept.</param>
/// <param name="PhotosRemoved">Photos the listing no longer shows, and that the item no longer shows either.</param>
/// <param name="Failures">Listings that could not be finished, with the reason.</param>
public sealed record PhotoSyncOutcome(
    int Listings,
    int ItemsUpdated,
    int PhotosAdded,
    int PhotosRemoved,
    IReadOnlyList<string> Failures);

/// <summary>
/// Keeps an inventory item's photos in step with the marketplace listing it is linked to, for the accounts whose
/// operator asked for it.
///
/// The listing is the truth: its photos become the item's photos, in its order, with the first as the hero. That
/// is the whole point of the setting — the pictures a buyer sees on the marketplace are the pictures the item and
/// the storefront show — so photos the item held that the listing does not show are let go.
///
/// Two things keep that from being destructive. A photo already held for an address is kept rather than fetched
/// again, so photos keep their addresses and a storefront page's images do not break. And nothing is removed until
/// the replacement is in hand: a marketplace that will not answer leaves the item exactly as it was, to be tried
/// again later.
/// </summary>
public sealed class InventoryPhotoSyncService(
    IInventoryPhotoSyncStore store,
    IRemoteImageFetcher remoteImages,
    IInventoryPhotoProcessor processor,
    TimeProvider clock)
{
    /// <summary>Takes the listings waiting longest and puts their photos onto their items.</summary>
    public async Task<PhotoSyncOutcome> SyncPendingAsync(int take, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyList<PlatformListing> pending = await store.ListPendingAsync(take, now.AddHours(-1), cancellationToken);
        int items = 0, added = 0, removed = 0;
        List<string> failures = [];

        foreach (PlatformListing listing in pending)
        {
            PhotoSyncStep step = await SyncAsync(listing, now, cancellationToken);
            items += step.ItemUpdated ? 1 : 0;
            added += step.PhotosAdded;
            removed += step.PhotosRemoved;
            if (step.Failure is string failure)
            {
                failures.Add(failure);
            }
        }

        return new PhotoSyncOutcome(pending.Count, items, added, removed, failures);
    }

    private sealed record PhotoSyncStep(bool ItemUpdated, int PhotosAdded, int PhotosRemoved, string? Failure);

    private async Task<PhotoSyncStep> SyncAsync(PlatformListing listing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        InventoryItem? item = listing.ReconciledInventoryItem;
        List<string> wanted = InventoryPhotoSet.ParseReferences(listing.PhotoUrlsJson);
        if (item is null || listing.PhotoUrlsKey is null || wanted.Count == 0)
        {
            // Nothing to hand over. Marking it done keeps it out of the queue until its photos change.
            return await FinishAsync(listing, now, error: null, cancellationToken);
        }

        // An item listed on more than one marketplace takes its photos from the listing that shows the most of
        // them, and the most recently changed of those. Every other listing stands aside until its own photos
        // change, so two accounts cannot take turns overwriting one item.
        IReadOnlyList<PlatformListing> sources = await store.ListPhotoSourcesForItemAsync(item.Id, cancellationToken);
        PlatformListing owner = sources
            .OrderByDescending(candidate => InventoryPhotoSet.ParseReferences(candidate.PhotoUrlsJson).Count)
            .ThenByDescending(candidate => candidate.PhotoUrlsChangedAt ?? DateTimeOffset.MinValue)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault() ?? listing;
        if (owner.Id != listing.Id)
        {
            return await FinishAsync(listing, now, error: null, cancellationToken);
        }

        if (item.PhotoSourceKey == ListingPhotoRules.SourceKey(listing.Platform, listing.ExternalListingId, listing.PhotoUrlsKey))
        {
            return await FinishAsync(listing, now, error: null, cancellationToken);
        }

        // What the item shows now, before anything is fetched: a photo added below joins the item's own collection
        // as it is tracked, so the two are compared from here rather than from a collection that grows underfoot.
        List<InventoryPhoto> before = [.. item.Photos];
        List<InventoryPhoto> ordered = [];
        int fetched = 0, unreachable = 0;
        foreach (string address in wanted)
        {
            InventoryPhoto? held = before.FirstOrDefault(photo => photo.SourceUrl == address);
            if (held is not null)
            {
                ordered.Add(held);
                continue;
            }

            InventoryPhoto? taken = await FetchAsync(listing.Platform, item, address, cancellationToken);
            if (taken is null)
            {
                unreachable++;
                continue;
            }

            store.AddPhoto(taken);
            ordered.Add(taken);
            fetched++;
        }

        if (ordered.Count == 0)
        {
            // Every photo failed. The item keeps what it has rather than being emptied over a bad connection.
            string failure = $"{listing.Platform} listing {listing.ExternalListingId}: none of its {wanted.Count} photos could be fetched.";
            await FinishAsync(listing, now, failure, cancellationToken);
            return new PhotoSyncStep(false, 0, 0, failure);
        }

        List<InventoryPhoto> letGo = before.Where(photo => !ordered.Contains(photo)).ToList();
        foreach (InventoryPhoto photo in letGo)
        {
            store.RemovePhoto(photo);
            item.Photos.Remove(photo);
        }

        bool rearranged = !before.Where(photo => !letGo.Contains(photo)).SequenceEqual(ordered);
        InventoryPhotoSet.Arrange(item, ordered);
        item.PhotoSourceKey = ListingPhotoRules.SourceKey(listing.Platform, listing.ExternalListingId, listing.PhotoUrlsKey);
        string? partly = unreachable == 0 ? null : $"{unreachable} of {wanted.Count} photos could not be fetched.";
        await FinishAsync(listing, now, partly, cancellationToken);
        bool changed = fetched > 0 || letGo.Count > 0 || rearranged;
        return new PhotoSyncStep(changed, fetched, letGo.Count, partly is null ? null : $"{listing.Platform} listing {listing.ExternalListingId}: {partly}");
    }

    /// <summary>Fetches one photo, or null when it cannot be had — a strange address, an unreachable host, an unreadable image.</summary>
    private async Task<InventoryPhoto?> FetchAsync(string platform, InventoryItem item, string address, CancellationToken cancellationToken)
    {
        if (!ListingPhotoRules.IsTrustedImageAddress(platform, address))
        {
            return null;
        }

        try
        {
            PhotoUpload upload = await remoteImages.FetchAsync(address, cancellationToken);
            await using Stream content = upload.Content;
            ProcessedPhoto processed = await processor.ProcessAsync(upload, cancellationToken, ListingPhotoRules.MaxPixels);
            return new InventoryPhoto
            {
                OrganizationId = item.OrganizationId,
                InventoryItemId = item.Id,
                SourceUrl = address,
                OriginalFileName = processed.OriginalFileName,
                ContentType = processed.ContentType,
                OriginalBytes = processed.OriginalBytes,
                ThumbnailBytes = processed.ThumbnailBytes,
                OriginalByteSize = processed.OriginalByteSize,
                ThumbnailByteSize = processed.ThumbnailByteSize,
                Width = processed.Width,
                Height = processed.Height,
            };
        }
        catch (RemoteImageFetchException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // The processor's way of saying the bytes were not a usable image.
            return null;
        }
    }

    private async Task<PhotoSyncStep> FinishAsync(PlatformListing listing, DateTimeOffset now, string? error, CancellationToken cancellationToken)
    {
        listing.PhotoSyncedKey = listing.PhotoUrlsKey;
        listing.PhotosSyncedAt = now;
        listing.PhotoSyncError = error;
        await store.SaveChangesAsync(cancellationToken);
        return new PhotoSyncStep(false, 0, 0, null);
    }
}
