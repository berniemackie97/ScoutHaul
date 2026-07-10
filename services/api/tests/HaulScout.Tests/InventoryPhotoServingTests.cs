using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

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
        InventoryController controller = new(db, null!, null!, null!);

        IActionResult result = await controller.GetPhotoThumbnail(itemId, photoId, CancellationToken.None);

        FileContentResult file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(new byte[] { 4, 5, 6 }, file.FileContents);
    }
}
