using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Api.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        FormFile file = new(stream, 0, pngBytes.Length, "file", "item.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        InventoryPhotoStorageService.ProcessedInventoryPhoto result = await service.ProcessAsync(file, CancellationToken.None);

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("item.png", result.OriginalFileName);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(pngBytes.Length, result.OriginalByteSize);
        Assert.NotEmpty(result.OriginalBytes);
        Assert.NotEmpty(result.ThumbnailBytes);
        Assert.True(result.ThumbnailByteSize > 0);
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
