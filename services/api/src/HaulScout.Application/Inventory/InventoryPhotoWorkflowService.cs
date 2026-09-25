using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

public sealed class InventoryPhotoWorkflowService(
    IInventoryPhotoStore photos,
    ICurrentUserContext currentUser,
    IInventoryPhotoProcessor photoStorage,
    IRemoteImageFetcher remoteImages)
{
    public sealed record InventoryPhotoFile(byte[] Bytes, string ContentType);

    /// <summary>
    /// Attaches an image that lives on the web (the operator's own listing on eBay/Vinted/etc.) by URL,
    /// so photos don't have to be saved to a device first. The bytes are fetched, then handed to the
    /// same upload path as a file — identical validation, resizing, hero and ordering rules.
    /// </summary>
    public async Task<AppResult<InventoryPhotoUploadResultDto>> UploadFromUrlAsync(
        Guid id,
        string? url,
        bool isHero,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return AppResult<InventoryPhotoUploadResultDto>.Validation("An image URL is required.");
        }

        // Check the item exists before making an outbound request for it.
        if (await FindItemAsync(id, cancellationToken) is null)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound();
        }

        PhotoUpload upload;
        try
        {
            upload = await remoteImages.FetchAsync(url, cancellationToken);
        }
        catch (RemoteImageFetchException exception)
        {
            return AppResult<InventoryPhotoUploadResultDto>.Validation(exception.Message);
        }

        await using Stream content = upload.Content;
        return await UploadAsync(id, upload, isHero, cancellationToken);
    }

    public async Task<AppResult<InventoryPhotoUploadResultDto>> UploadAsync(
        Guid id,
        PhotoUpload? file,
        bool isHero,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItemAsync(id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound();
        }

        if (file is null)
        {
            return AppResult<InventoryPhotoUploadResultDto>.Validation("Photo file is required.");
        }

        ProcessedPhoto processed;
        try
        {
            processed = await photoStorage.ProcessAsync(file, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return AppResult<InventoryPhotoUploadResultDto>.Validation(exception.Message);
        }

        if (isHero || item.Photos.Count == 0)
        {
            foreach (InventoryPhoto existingPhoto in item.Photos)
            {
                existingPhoto.IsHero = false;
            }
        }

        InventoryPhoto photo = new()
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = item.Id,
            IsHero = isHero || item.Photos.Count == 0,
            SortOrder = item.Photos.Count == 0 ? 0 : item.Photos.Max(existing => existing.SortOrder) + 1,
            OriginalFileName = processed.OriginalFileName,
            ContentType = processed.ContentType,
            OriginalBytes = processed.OriginalBytes,
            ThumbnailBytes = processed.ThumbnailBytes,
            OriginalByteSize = processed.OriginalByteSize,
            ThumbnailByteSize = processed.ThumbnailByteSize,
            Width = processed.Width,
            Height = processed.Height
        };

        await photos.AddAsync(photo, cancellationToken);

        List<string> photoUrls = InventoryPhotoSet.ParseReferences(item.PhotoLocalReferencesJson);
        string uploadedUrl = InventoryPhotoSet.OriginalUrl(item.Id, photo.Id);
        photoUrls.RemoveAll(url => string.Equals(url, uploadedUrl, StringComparison.OrdinalIgnoreCase));
        if (photo.IsHero || photoUrls.Count == 0)
        {
            photoUrls.Insert(0, uploadedUrl);
        }
        else
        {
            photoUrls.Add(uploadedUrl);
        }

        item.PhotoLocalReferencesJson = InventoryPhotoSet.Serialize(photoUrls);
        await photos.SaveChangesAsync(cancellationToken);

        await photos.LoadPhotosAsync(item, cancellationToken);
        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, uploadedUrl, InventoryPhotoSet.Ordered(item).Select(item => item.ToDto()).ToArray()));
    }

    public async Task<AppResult<InventoryPhotoUploadResultDto>> DeleteByUrlAsync(
        Guid id,
        string url,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItemAsync(id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound();
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return AppResult<InventoryPhotoUploadResultDto>.Validation("Photo URL is required.");
        }

        InventoryPhoto? photo = ResolvePhotoByUrl(item, url);
        if (photo is not null)
        {
            photos.Remove(photo);
        }

        List<string> photoUrls = InventoryPhotoSet.ParseReferences(item.PhotoLocalReferencesJson);
        bool removedLegacyUrl = photoUrls.RemoveAll(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase)) > 0;
        if (photo is not null)
        {
            photoUrls.RemoveAll(existing =>
                string.Equals(existing, InventoryPhotoSet.OriginalUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing, InventoryPhotoSet.ThumbnailUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase));
        }

        if (photo is null && !removedLegacyUrl)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound("Photo was not found on this inventory item.");
        }

        item.PhotoLocalReferencesJson = photoUrls.Count == 0 ? null : InventoryPhotoSet.Serialize(photoUrls);
        if (photo is null)
        {
            await photoStorage.DeleteAsync(currentUser.OrganizationId, item.Id, url);
        }

        if (photo?.IsHero == true)
        {
            InventoryPhoto? nextHero = item.Photos
                .Where(candidate => candidate.Id != photo.Id)
                .OrderBy(candidate => candidate.SortOrder)
                .ThenBy(candidate => candidate.CreatedAt)
                .FirstOrDefault();
            if (nextHero is not null)
            {
                nextHero.IsHero = true;
            }
        }

        await photos.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, InventoryPhotoSet.Ordered(item).Where(candidate => candidate.Id != photo?.Id).Select(candidate => candidate.ToDto()).ToArray()));
    }

    public async Task<AppResult<InventoryPhotoUploadResultDto>> SetHeroAsync(
        Guid id,
        string url,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItemAsync(id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound();
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return AppResult<InventoryPhotoUploadResultDto>.Validation("Photo URL is required.");
        }

        InventoryPhoto? photo = ResolvePhotoByUrl(item, url);
        if (photo is not null)
        {
            foreach (InventoryPhoto existingPhoto in item.Photos)
            {
                existingPhoto.IsHero = existingPhoto.Id == photo.Id;
            }
        }

        List<string> photoUrls = InventoryPhotoSet.ParseReferences(item.PhotoLocalReferencesJson);
        int index = photoUrls.FindIndex(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && photo is not null)
        {
            url = InventoryPhotoSet.OriginalUrl(item.Id, photo.Id);
            index = photoUrls.FindIndex(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                photoUrls.Insert(0, url);
                index = 0;
            }
        }

        if (index < 0)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound("Photo was not found on this inventory item.");
        }

        photoUrls.RemoveAt(index);
        photoUrls.Insert(0, url);
        item.PhotoLocalReferencesJson = InventoryPhotoSet.Serialize(photoUrls);
        await photos.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, InventoryPhotoSet.Ordered(item).Select(item => item.ToDto()).ToArray()));
    }

    public async Task<AppResult<InventoryPhotoUploadResultDto>> DeleteByIdAsync(
        Guid id,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItemAsync(id, cancellationToken);
        if (item is null)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound();
        }

        InventoryPhoto? photo = item.Photos.SingleOrDefault(candidate => candidate.Id == photoId);
        if (photo is null)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound("Photo was not found on this inventory item.");
        }

        photos.Remove(photo);
        List<string> photoUrls = InventoryPhotoSet.ParseReferences(item.PhotoLocalReferencesJson);
        photoUrls.RemoveAll(existing =>
            string.Equals(existing, InventoryPhotoSet.OriginalUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase)
            || string.Equals(existing, InventoryPhotoSet.ThumbnailUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase));
        item.PhotoLocalReferencesJson = photoUrls.Count == 0 ? null : InventoryPhotoSet.Serialize(photoUrls);

        if (photo.IsHero)
        {
            InventoryPhoto? nextHero = item.Photos
                .Where(candidate => candidate.Id != photo.Id)
                .OrderBy(candidate => candidate.SortOrder)
                .ThenBy(candidate => candidate.CreatedAt)
                .FirstOrDefault();
            if (nextHero is not null)
            {
                nextHero.IsHero = true;
            }
        }

        await photos.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, InventoryPhotoSet.Ordered(item).Where(candidate => candidate.Id != photo.Id).Select(candidate => candidate.ToDto()).ToArray()));
    }

    public async Task<InventoryPhotoFile?> GetThumbnailAsync(Guid itemId, Guid photoId, CancellationToken cancellationToken)
    {
        InventoryPhoto? photo = await FindPhotoUnscopedAsync(itemId, photoId, cancellationToken);
        return photo is null ? null : new InventoryPhotoFile(photo.ThumbnailBytes, "image/jpeg");
    }

    public async Task<InventoryPhotoFile?> GetOriginalAsync(Guid itemId, Guid photoId, CancellationToken cancellationToken)
    {
        InventoryPhoto? photo = await FindPhotoUnscopedAsync(itemId, photoId, cancellationToken);
        return photo is null ? null : new InventoryPhotoFile(photo.OriginalBytes, photo.ContentType);
    }

    private Task<InventoryItem?> FindItemAsync(Guid id, CancellationToken cancellationToken) =>
        photos.FindItemWithPhotosAsync(currentUser.OrganizationId, id, cancellationToken);

    private Task<InventoryPhoto?> FindPhotoUnscopedAsync(Guid itemId, Guid photoId, CancellationToken cancellationToken) =>
        photos.FindPhotoUnscopedAsync(itemId, photoId, cancellationToken);

    private static InventoryPhoto? ResolvePhotoByUrl(InventoryItem item, string url)
    {
        if (TryExtractPhotoId(url, out Guid photoId))
        {
            return item.Photos.SingleOrDefault(photo => photo.Id == photoId);
        }

        return null;
    }

    private static bool TryExtractPhotoId(string url, out Guid photoId)
    {
        photoId = default;
        string[] parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int photosIndex = Array.FindIndex(parts, part => string.Equals(part, "photos", StringComparison.OrdinalIgnoreCase));
        return photosIndex >= 0
            && photosIndex + 1 < parts.Length
            && Guid.TryParse(parts[photosIndex + 1], out photoId);
    }

}
