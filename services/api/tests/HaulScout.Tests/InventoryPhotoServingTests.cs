using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Tests;

public class InventoryPhotoServingTests
{
    [Fact]
    public async Task Photo_thumbnail_is_served_to_anonymous_requests_without_a_tenant()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Photo_thumbnail_is_served_to_anonymous_requests_without_a_tenant))
            .Options;

        Guid itemId, photoId;
        TenantContext seedTenant = new();
        seedTenant.SetOrganization(org);
        await using (HaulScoutDbContext seed = new(options, seedTenant))
        {
            InventoryItem item = new() { Title = "Reshiram" };
            seed.InventoryItems.Add(item);
            InventoryPhoto photo = new()
            {
                InventoryItemId = item.Id,
                ContentType = "image/jpeg",
                OriginalBytes = [1, 2, 3],
                ThumbnailBytes = [4, 5, 6],
            };
            seed.InventoryPhotos.Add(photo);
            await seed.SaveChangesAsync();
            itemId = item.Id;
            photoId = photo.Id;
        }

        // Anonymous request: no organization resolved on the tenant — exactly like an <img>/Image
        // fetching the photo URL with no bearer token. Must still resolve via IgnoreQueryFilters.
        TenantContext anonTenant = new();
        await using HaulScoutDbContext db = new(options, anonTenant);
        InventoryController controller = new(
            null!,
            null!,
            null!,
            null!,
            null!,
            new InventoryPhotoWorkflowService(new EfInventoryPhotoStore(db), null!, null!, new DatabaseInventoryPhotoBlobStore(), null!),
            null!,
            null!,
            null!);

        IActionResult result = await controller.GetPhotoThumbnail(itemId, photoId, CancellationToken.None);

        FileContentResult file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(new byte[] { 4, 5, 6 }, file.FileContents);
    }

    [Fact]
    public async Task A_photo_whose_images_live_in_object_storage_is_served_the_same_way()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(A_photo_whose_images_live_in_object_storage_is_served_the_same_way))
            .Options;

        FakePhotoObjects objects = new();
        Guid itemId, photoId;
        TenantContext seedTenant = new();
        seedTenant.SetOrganization(org);
        await using (HaulScoutDbContext seed = new(options, seedTenant))
        {
            InventoryItem item = new() { Title = "Zekrom" };
            seed.InventoryItems.Add(item);
            InventoryPhoto photo = new()
            {
                InventoryItemId = item.Id,
                ContentType = "image/webp",
                // Moved: the row holds keys, not images.
                OriginalObjectKey = "org/photo/original.webp",
                ThumbnailObjectKey = "org/photo/thumbnail.jpg",
            };
            seed.InventoryPhotos.Add(photo);
            await seed.SaveChangesAsync();
            itemId = item.Id;
            photoId = photo.Id;
        }

        await objects.PutAsync("org/photo/original.webp", [7, 7, 7], "image/webp", CancellationToken.None);
        await objects.PutAsync("org/photo/thumbnail.jpg", [8, 8], "image/jpeg", CancellationToken.None);

        TenantContext anonTenant = new();
        await using HaulScoutDbContext db = new(options, anonTenant);
        InventoryController controller = new(
            null!,
            null!,
            null!,
            null!,
            null!,
            new InventoryPhotoWorkflowService(
                new EfInventoryPhotoStore(db),
                null!,
                null!,
                new ObjectStoreInventoryPhotoBlobStore(
                    objects,
                    Options.Create(new PhotoObjectStorageOptions { Bucket = "photos" }),
                    NullLogger<ObjectStoreInventoryPhotoBlobStore>.Instance),
                null!),
            null!,
            null!,
            null!);

        FileContentResult thumbnail = Assert.IsType<FileContentResult>(await controller.GetPhotoThumbnail(itemId, photoId, CancellationToken.None));
        FileContentResult original = Assert.IsType<FileContentResult>(await controller.GetPhotoOriginal(itemId, photoId, CancellationToken.None));

        Assert.Equal(new byte[] { 8, 8 }, thumbnail.FileContents);
        Assert.Equal("image/jpeg", thumbnail.ContentType);
        Assert.Equal(new byte[] { 7, 7, 7 }, original.FileContents);
        Assert.Equal("image/webp", original.ContentType);
    }
}
