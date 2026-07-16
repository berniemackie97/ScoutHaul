using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

public sealed class InventoryPhotoWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    InventoryPhotoStorageService photoStorage)
{
    public sealed record InventoryPhotoFile(byte[] Bytes, string ContentType);

    public async Task<AppResult<InventoryPhotoUploadResultDto>> UploadAsync(
        Guid id,
        IFormFile? file,
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

        InventoryPhotoStorageService.ProcessedInventoryPhoto processed;
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

        db.InventoryPhotos.Add(photo);

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        string uploadedUrl = PhotoOriginalUrl(item.Id, photo.Id);
        photoUrls.RemoveAll(url => string.Equals(url, uploadedUrl, StringComparison.OrdinalIgnoreCase));
        if (photo.IsHero || photoUrls.Count == 0)
        {
            photoUrls.Insert(0, uploadedUrl);
        }
        else
        {
            photoUrls.Add(uploadedUrl);
        }

        item.PhotoLocalReferencesJson = JsonSerializer.Serialize(photoUrls);
        await db.SaveChangesAsync(cancellationToken);

        await db.Entry(item).Collection(existing => existing.Photos).LoadAsync(cancellationToken);
        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, uploadedUrl, OrderedPhotos(item).Select(item => item.ToDto()).ToArray()));
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
            db.InventoryPhotos.Remove(photo);
        }

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        bool removedLegacyUrl = photoUrls.RemoveAll(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase)) > 0;
        if (photo is not null)
        {
            photoUrls.RemoveAll(existing =>
                string.Equals(existing, PhotoOriginalUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing, PhotoThumbnailUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase));
        }

        if (photo is null && !removedLegacyUrl)
        {
            return AppResult<InventoryPhotoUploadResultDto>.NotFound("Photo was not found on this inventory item.");
        }

        item.PhotoLocalReferencesJson = photoUrls.Count == 0 ? null : JsonSerializer.Serialize(photoUrls);
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

        await db.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, OrderedPhotos(item).Where(candidate => candidate.Id != photo?.Id).Select(candidate => candidate.ToDto()).ToArray()));
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

        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        int index = photoUrls.FindIndex(existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && photo is not null)
        {
            url = PhotoOriginalUrl(item.Id, photo.Id);
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
        item.PhotoLocalReferencesJson = JsonSerializer.Serialize(photoUrls);
        await db.SaveChangesAsync(cancellationToken);

        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, OrderedPhotos(item).Select(item => item.ToDto()).ToArray()));
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

        db.InventoryPhotos.Remove(photo);
        List<string> photoUrls = ParsePhotoUrls(item.PhotoLocalReferencesJson);
        photoUrls.RemoveAll(existing =>
            string.Equals(existing, PhotoOriginalUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase)
            || string.Equals(existing, PhotoThumbnailUrl(item.Id, photo.Id), StringComparison.OrdinalIgnoreCase));
        item.PhotoLocalReferencesJson = photoUrls.Count == 0 ? null : JsonSerializer.Serialize(photoUrls);

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

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryPhotoUploadResultDto>.Ok(
            new InventoryPhotoUploadResultDto(item.Id, photoUrls, UploadedUrl: null, OrderedPhotos(item).Where(candidate => candidate.Id != photo.Id).Select(candidate => candidate.ToDto()).ToArray()));
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
        db.InventoryItems
            .Include(item => item.Photos)
            .Include(item => item.Issues)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
                .ThenInclude(item => item.ComponentInventoryItem)
            .SingleOrDefaultAsync(
                item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

    private Task<InventoryPhoto?> FindPhotoUnscopedAsync(Guid itemId, Guid photoId, CancellationToken cancellationToken) =>
        db.InventoryPhotos
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.Id == photoId && item.InventoryItemId == itemId,
                cancellationToken);

    private static IReadOnlyList<InventoryPhoto> OrderedPhotos(InventoryItem item) =>
        item.Photos
            .OrderByDescending(photo => photo.IsHero)
            .ThenBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .ToArray();

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

    private static string PhotoOriginalUrl(Guid itemId, Guid photoId) => $"/api/inventory/{itemId}/photos/{photoId}/original";

    private static string PhotoThumbnailUrl(Guid itemId, Guid photoId) => $"/api/inventory/{itemId}/photos/{photoId}/thumbnail";

    private static List<string> ParsePhotoUrls(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
