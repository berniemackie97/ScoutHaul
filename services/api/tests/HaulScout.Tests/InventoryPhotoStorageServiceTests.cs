using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Api.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using HaulScout.Application.Inventory;

namespace HaulScout.Tests;

public sealed class InventoryPhotoStorageServiceTests
{
    [Fact]
    public async Task ProcessAsync_CreatesThumbnailBytesAndMetadata()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:InventoryPhotos:MaxBytes"] = "8000000",
                ["Storage:InventoryPhotos:ThumbnailMaxPixels"] = "120"
            })
            .Build();
        InventoryPhotoStorageService service = new(
            new TestWebHostEnvironment(),
            configuration,
            NullLogger<InventoryPhotoStorageService>.Instance);
        byte[] pngBytes;
        using (Image<Rgba32> image = new(2, 2, Color.Red))
        {
            await using MemoryStream pngStream = new();
            await image.SaveAsPngAsync(pngStream);
            pngBytes = pngStream.ToArray();
        }

        await using MemoryStream stream = new(pngBytes);
        // No ASP.NET FormFile needed any more: the processor takes a plain description of the upload.
        PhotoUpload upload = new(stream, "item.png", "image/png", pngBytes.Length);

        ProcessedPhoto result = await service.ProcessAsync(upload, CancellationToken.None);

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("item.png", result.OriginalFileName);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(pngBytes.Length, result.OriginalByteSize);
        Assert.NotEmpty(result.OriginalBytes);
        Assert.NotEmpty(result.ThumbnailBytes);
        Assert.True(result.ThumbnailByteSize > 0);
    }

    [Fact]
    public async Task A_TIFF_is_refused_even_when_it_claims_to_be_a_JPEG()
    {
        InventoryPhotoStorageService service = new(
            new TestWebHostEnvironment(),
            new ConfigurationBuilder().Build(),
            NullLogger<InventoryPhotoStorageService>.Instance);
        byte[] tiffBytes;
        using (Image<Rgba32> image = new(2, 2, Color.Red))
        {
            await using MemoryStream tiffStream = new();
            await image.SaveAsTiffAsync(tiffStream);
            tiffBytes = tiffStream.ToArray();
        }

        await using MemoryStream stream = new(tiffBytes);
        PhotoUpload upload = new(stream, "item.jpg", "image/jpeg", tiffBytes.Length);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(upload, CancellationToken.None));
        Assert.Contains("could not be decoded", refused.Message);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HaulScout.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
