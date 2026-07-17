using Microsoft.AspNetCore.Hosting;
using HaulScout.Application.Inventory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace HaulScout.Api.Services;

/// <summary>
/// The host-side half of <see cref="IInventoryPhotoProcessor"/>. It stays in the web assembly because deleting a
/// legacy on-disk photo needs the host's web root; nothing about it reaches into the Application layer, which sees
/// only the port.
/// </summary>
public sealed class InventoryPhotoStorageService(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<InventoryPhotoStorageService> logger) : IInventoryPhotoProcessor
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public long MaxBytes => configuration.GetValue("Storage:InventoryPhotos:MaxBytes", 8_000_000);
    public int ThumbnailMaxPixels => configuration.GetValue("Storage:InventoryPhotos:ThumbnailMaxPixels", 360);

    public async Task<ProcessedPhoto> ProcessAsync(PhotoUpload upload, CancellationToken cancellationToken)
    {
        if (upload.Length <= 0)
        {
            throw new InvalidOperationException("Photo file is empty.");
        }

        if (upload.Length > MaxBytes)
        {
            throw new InvalidOperationException($"Photo exceeds the {MaxBytes / 1_000_000} MB upload limit.");
        }

        if (upload.ContentType is null || !AllowedContentTypes.Contains(upload.ContentType))
        {
            throw new InvalidOperationException("Only JPEG, PNG, and WebP photos are supported.");
        }

        await using MemoryStream originalStream = new();
        await upload.Content.CopyToAsync(originalStream, cancellationToken);
        byte[] originalBytes = originalStream.ToArray();

        originalStream.Position = 0;
        try
        {
            using Image image = await Image.LoadAsync(originalStream, cancellationToken);
            int width = image.Width;
            int height = image.Height;

            image.Mutate(operation => operation.AutoOrient());
            image.Mutate(operation => operation.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(ThumbnailMaxPixels, ThumbnailMaxPixels)
            }));

            await using MemoryStream thumbnailStream = new();
            await image.SaveAsJpegAsync(thumbnailStream, new JpegEncoder { Quality = 74 }, cancellationToken);
            byte[] thumbnailBytes = thumbnailStream.ToArray();

            return new ProcessedPhoto(
                originalBytes,
                thumbnailBytes,
                upload.ContentType,
                string.IsNullOrWhiteSpace(upload.FileName) ? null : Path.GetFileName(upload.FileName),
                originalBytes.LongLength,
                thumbnailBytes.LongLength,
                width,
                height);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidOperationException("Photo could not be decoded. Upload a valid JPEG, PNG, or WebP image.", exception);
        }
    }

    public Task DeleteAsync(Guid organizationId, Guid inventoryItemId, string url)
    {
        string expectedPrefix = $"/uploads/inventory/{organizationId:N}/{inventoryItemId:N}/";
        if (!url.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Skipped deleting inventory photo outside managed path. OrganizationId={OrganizationId} ItemId={ItemId} Url={Url}", organizationId, inventoryItemId, url);
            return Task.CompletedTask;
        }

        string relativePath = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string absolutePath = Path.Combine(WebRootPath(), relativePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    private string WebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(environment.WebRootPath))
        {
            return environment.WebRootPath;
        }

        string fallback = Path.Combine(environment.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
