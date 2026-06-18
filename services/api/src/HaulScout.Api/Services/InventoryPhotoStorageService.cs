using Microsoft.AspNetCore.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace HaulScout.Api.Services;

public sealed class InventoryPhotoStorageService(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<InventoryPhotoStorageService> logger)
{
    public sealed record ProcessedInventoryPhoto(
        byte[] OriginalBytes,
        byte[] ThumbnailBytes,
        string ContentType,
        string? OriginalFileName,
        long OriginalByteSize,
        long ThumbnailByteSize,
        int Width,
        int Height);

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public long MaxBytes => configuration.GetValue("Storage:InventoryPhotos:MaxBytes", 8_000_000);
    public int ThumbnailMaxPixels => configuration.GetValue("Storage:InventoryPhotos:ThumbnailMaxPixels", 360);

    public async Task<ProcessedInventoryPhoto> ProcessAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Photo file is empty.");
        }

        if (file.Length > MaxBytes)
        {
            throw new InvalidOperationException($"Photo exceeds the {MaxBytes / 1_000_000} MB upload limit.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException("Only JPEG, PNG, and WebP photos are supported.");
        }

        await using MemoryStream originalStream = new();
        await file.CopyToAsync(originalStream, cancellationToken);
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

            return new ProcessedInventoryPhoto(
                originalBytes,
                thumbnailBytes,
                file.ContentType,
                string.IsNullOrWhiteSpace(file.FileName) ? null : Path.GetFileName(file.FileName),
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
