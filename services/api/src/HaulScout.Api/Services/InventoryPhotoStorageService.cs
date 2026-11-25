using Microsoft.AspNetCore.Hosting;
using HaulScout.Application.Inventory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
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

    // Only the formats accepted above are decoded, whatever the bytes turn out to be: ImageSharp sniffs the real format,
    // and its TIFF code is where known flaws live (see the suppressions in Directory.Build.props).
    private static readonly DecoderOptions Decoding = new()
    {
        Configuration = new SixLabors.ImageSharp.Configuration(new JpegConfigurationModule(), new PngConfigurationModule(), new WebpConfigurationModule()),
    };

    public long MaxBytes => configuration.GetValue("Storage:InventoryPhotos:MaxBytes", 8_000_000);
    public int ThumbnailMaxPixels => configuration.GetValue("Storage:InventoryPhotos:ThumbnailMaxPixels", 360);

    public async Task<ProcessedPhoto> ProcessAsync(PhotoUpload upload, CancellationToken cancellationToken, int? maxPixels = null)
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

        string uploadedContentType = upload.ContentType;
        await using MemoryStream originalStream = new();
        await upload.Content.CopyToAsync(originalStream, cancellationToken);
        originalStream.Position = 0;
        try
        {
            using Image image = await Image.LoadAsync(Decoding, originalStream, cancellationToken);
            image.Mutate(operation => operation.AutoOrient());
            StripMetadata(image);
            bool resized = false;
            if (maxPixels is int ceiling && (image.Width > ceiling || image.Height > ceiling))
            {
                image.Mutate(operation => operation.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(ceiling, ceiling)
                }));
                resized = true;
            }

            string contentType = resized ? "image/jpeg" : uploadedContentType;
            await using MemoryStream sanitizedStream = new();
            switch (contentType.ToLowerInvariant())
            {
                case "image/png":
                    await image.SaveAsPngAsync(sanitizedStream, new PngEncoder { SkipMetadata = true }, cancellationToken);
                    break;
                case "image/webp":
                    await image.SaveAsWebpAsync(sanitizedStream, new WebpEncoder { Quality = 82, SkipMetadata = true }, cancellationToken);
                    break;
                default:
                    await image.SaveAsJpegAsync(sanitizedStream, new JpegEncoder { Quality = 82, SkipMetadata = true }, cancellationToken);
                    contentType = "image/jpeg";
                    break;
            }
            byte[] originalBytes = sanitizedStream.ToArray();
            int width = image.Width;
            int height = image.Height;

            image.Mutate(operation => operation.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(ThumbnailMaxPixels, ThumbnailMaxPixels)
            }));

            await using MemoryStream thumbnailStream = new();
            await image.SaveAsJpegAsync(thumbnailStream, new JpegEncoder { Quality = 74, SkipMetadata = true }, cancellationToken);
            byte[] thumbnailBytes = thumbnailStream.ToArray();

            return new ProcessedPhoto(
                originalBytes,
                thumbnailBytes,
                contentType,
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

    // Encoders' SkipMetadata alone has let EXIF (GPS included) through; drop the profiles themselves.
    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
        foreach (ImageFrame frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.XmpProfile = null;
            frame.Metadata.IptcProfile = null;
        }
    }
}
