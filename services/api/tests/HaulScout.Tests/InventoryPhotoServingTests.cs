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
        var org = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Photo_thumbnail_is_served_to_anonymous_requests_without_a_tenant))
            .Options;

        Guid itemId, photoId;
        var seedTenant = new TenantContext();
        seedTenant.SetOrganization(org);
        await using (var seed = new HaulScoutDbContext(options, seedTenant))
        {
            var item = new InventoryItem { Title = "Reshiram" };
            seed.InventoryItems.Add(item);
            var photo = new InventoryPhoto
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
        var anonTenant = new TenantContext();
        await using var db = new HaulScoutDbContext(options, anonTenant);
        var controller = new InventoryController(db, null!, null!, null!);

        var result = await controller.GetPhotoThumbnail(itemId, photoId, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(new byte[] { 4, 5, 6 }, file.FileContents);
    }
}
