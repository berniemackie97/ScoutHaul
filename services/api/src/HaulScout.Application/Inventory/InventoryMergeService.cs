using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Folds duplicate records of one physical item — an import that created a second item instead of matching the
/// first — into the record the operator keeps. Everything the duplicates carried moves across and they are removed
/// in one transaction, with what each one was recorded in the audit log.
/// </summary>
public sealed class InventoryMergeService(
    IInventoryMergeStore store,
    IInventoryPhotoBlobStore blobs,
    IAuditTrail audit,
    ICurrentUserContext currentUser,
    IUnitOfWork unitOfWork)
{
    public const int MaxDuplicates = 20;

    public async Task<AppResult<MergeInventoryItemsResultDto>> MergeAsync(MergeInventoryItemsRequest request, CancellationToken cancellationToken)
    {
        Guid[] duplicateIds = (request.DuplicateIds ?? []).Where(id => id != request.KeepId).Distinct().ToArray();
        if (duplicateIds.Length == 0)
        {
            return AppResult<MergeInventoryItemsResultDto>.Validation("Choose at least one other item to merge into the one you keep.");
        }

        if (duplicateIds.Length > MaxDuplicates)
        {
            return AppResult<MergeInventoryItemsResultDto>.Validation($"Merge at most {MaxDuplicates} items at a time.");
        }

        (AppResult<MergeInventoryItemsResultDto> result, List<InventoryPhoto> dropped) =
            await unitOfWork.InTransactionAsync(token => MergeLockedAsync(request.KeepId, duplicateIds, token), cancellationToken);

        // Image bytes go only once the merge has committed, so a failed one never leaves a photo without its image.
        foreach (InventoryPhoto photo in dropped)
        {
            await blobs.ForgetAsync(photo, cancellationToken);
        }

        return result;
    }

    private async Task<(AppResult<MergeInventoryItemsResultDto> Result, List<InventoryPhoto> Dropped)> MergeLockedAsync(Guid keepId, Guid[] duplicateIds, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        IReadOnlyList<InventoryItem> items = await store.LockItemsAsync(organizationId, [keepId, .. duplicateIds], cancellationToken);
        InventoryItem? kept = items.FirstOrDefault(item => item.Id == keepId);
        List<InventoryItem> duplicates = items.Where(item => item.Id != keepId).ToList();
        if (kept is null || duplicates.Count != duplicateIds.Length)
        {
            return (AppResult<MergeInventoryItemsResultDto>.NotFound("One of those items no longer exists."), []);
        }

        if (await RefusalAsync(organizationId, kept, duplicates, cancellationToken) is string refusal)
        {
            return (AppResult<MergeInventoryItemsResultDto>.Conflict(refusal), []);
        }

        List<InventoryPhoto> dropped = [];
        foreach (InventoryItem duplicate in duplicates)
        {
            await audit.RecordAsync(nameof(InventoryItem), kept.Id, "InventoryItemsMerged", Details(kept, duplicate), cancellationToken);
            InventoryMerge.FillBlanks(kept, duplicate);
            kept.Status = InventoryMerge.MergedStatus(kept.Status, duplicate.Status);
            await store.MoveReferencesAsync(duplicate, kept, cancellationToken);
            dropped.AddRange(await MovePhotosAsync(duplicate, kept, cancellationToken));
            store.Remove(duplicate);
        }

        // No profile pass here: a merge moves what the records already hold, and an item whose category came in from a
        // marketplace unrecognised would read as the general profile and lose its size and colors.
        await store.SaveChangesAsync(cancellationToken);
        return (AppResult<MergeInventoryItemsResultDto>.Ok(new MergeInventoryItemsResultDto(kept.Id, duplicates.Count)), dropped);
    }

    /// <summary>
    /// Merging is for two records of one thing. Two live sales, two purchase lots or two storefront histories say the
    /// records are not that, and a bundle's parts are a bundle's business — so each is refused with what to do first.
    /// </summary>
    private async Task<string?> RefusalAsync(Guid organizationId, InventoryItem kept, IReadOnlyList<InventoryItem> duplicates, CancellationToken cancellationToken)
    {
        // Kept item first, then the duplicates in the order they are merged: the first storefront record stays.
        List<MergeFootprint> footprints = [];
        foreach (InventoryItem item in duplicates.Prepend(kept))
        {
            footprints.Add(await store.InspectAsync(organizationId, item.Id, cancellationToken));
        }

        if (duplicates.Prepend(kept).Any(item => item.IsBundle) || footprints.Any(footprint => footprint.InBundle))
        {
            return "Bundles and the items in them can't be merged. Take the item out of its bundle first.";
        }

        if (footprints.Sum(footprint => footprint.LiveSales) > 1)
        {
            return "More than one of these items has a live sale, so they are not the same item sold once. Correct or cancel the extra sale first.";
        }

        if (footprints.Count(footprint => footprint.InPurchaseLot) > 1)
        {
            return "More than one of these items belongs to a purchase lot. Remove the duplicate from its lot first, so the lot's cost stays right.";
        }

        IEnumerable<MergeFootprint> removedListings = footprints.Where(footprint => footprint.StoreListing is not null).Skip(1);
        if (removedListings.Any(footprint => footprint.StoreListingHasHistory || footprint.StoreListing == StoreListingStatus.Reserved))
        {
            return "More than one of these items has its own storefront history (carts, orders, offers or reviews). They can't be combined automatically.";
        }

        return null;
    }

    /// <summary>
    /// The duplicate's photos follow the kept item's. One the kept item already has from the same source is dropped
    /// and returned, so its image can be released once the merge is saved.
    /// </summary>
    private async Task<List<InventoryPhoto>> MovePhotosAsync(InventoryItem duplicate, InventoryItem kept, CancellationToken cancellationToken)
    {
        if (duplicate.Photos.Count == 0)
        {
            return [];
        }

        if (kept.Photos.Count == 0)
        {
            kept.PhotoSourceKey = duplicate.PhotoSourceKey;
        }

        List<InventoryPhoto> ordered = [.. InventoryPhotoSet.Ordered(kept)];
        List<InventoryPhoto> dropped = [];
        foreach (InventoryPhoto photo in InventoryPhotoSet.Ordered(duplicate))
        {
            InventoryPhoto? copy = photo.SourceUrl is null
                ? null
                : ordered.FirstOrDefault(existing => string.Equals(existing.SourceUrl, photo.SourceUrl, StringComparison.Ordinal));
            if (copy is not null)
            {
                await store.DropPhotoAsync(photo, copy, cancellationToken);
                dropped.Add(photo);
                continue;
            }

            photo.InventoryItemId = kept.Id;
            kept.Photos.Add(photo);
            ordered.Add(photo);
        }

        duplicate.Photos.Clear();
        InventoryPhotoSet.Arrange(kept, ordered);
        return dropped;
    }

    private static object Details(InventoryItem kept, InventoryItem duplicate) => new
    {
        Merged = new
        {
            duplicate.Id,
            duplicate.Title,
            Status = duplicate.Status.ToString(),
            duplicate.Brand,
            duplicate.Category,
            duplicate.PurchasePrice,
            duplicate.EstimatedSalePrice,
            duplicate.Barcode,
            duplicate.CreatedAt,
        },
        Into = new { kept.Id, kept.Title },
    };
}
