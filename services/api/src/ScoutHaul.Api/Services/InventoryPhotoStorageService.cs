using Microsoft.AspNetCore.Hosting;

namespace ScoutHaul.Api.Services;

public sealed class InventoryPhotoStorageService(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<InventoryPhotoStorageService> logger)
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
        "image/heif"
    };

    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif"
    };

    public long MaxBytes => configuration.GetValue("Storage:InventoryPhotos:MaxBytes", 8_000_000);

    public async Task<string> SaveAsync(Guid organizationId, Guid inventoryItemId, IFormFile file, CancellationToken cancellationToken)
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
            throw new InvalidOperationException("Only JPEG, PNG, WebP, HEIC, and HEIF photos are supported.");
        }

        string extension = ExtensionByContentType[file.ContentType];
        string relativeDirectory = Path.Combine("uploads", "inventory", organizationId.ToString("N"), inventoryItemId.ToString("N"));
        string absoluteDirectory = Path.Combine(WebRootPath(), relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        string fileName = $"{Guid.NewGuid():N}{extension}";
        string absolutePath = Path.Combine(absoluteDirectory, fileName);
        await using FileStream stream = File.Create(absolutePath);
        await file.CopyToAsync(stream, cancellationToken);

        string relativeUrl = "/" + Path.Combine(relativeDirectory, fileName).Replace(Path.DirectorySeparatorChar, '/');
        return relativeUrl;
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
