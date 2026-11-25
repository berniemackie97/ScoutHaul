using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public sealed class InventoryPhotoServingTests
{
    [Fact]
    public void InventoryPhotoEndpointsRequireAuthorization()
    {
        foreach (string name in new[] { nameof(InventoryController.GetPhotoThumbnail), nameof(InventoryController.GetPhotoOriginal) })
        {
            var method = typeof(InventoryController).GetMethod(name)!;
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        }
    }

    [Fact]
    public async Task InventoryPhotoIsAvailableOnlyWithinItsOrganization()
    {
        Guid owner = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(InventoryPhotoIsAvailableOnlyWithinItsOrganization)).Options;
        Guid itemId;
        Guid photoId;
        TenantContext seedTenant = new();
        seedTenant.SetOrganization(owner);
        await using (HaulScoutDbContext seed = new(options, seedTenant))
        {
            InventoryItem item = new() { OrganizationId = owner, Title = "Private item" };
            InventoryPhoto photo = new()
            {
                OrganizationId = owner,
                InventoryItemId = item.Id,
                ContentType = "image/jpeg",
                OriginalBytes = [1, 2, 3],
                ThumbnailBytes = [4, 5, 6],
            };
            seed.InventoryItems.Add(item);
            seed.InventoryPhotos.Add(photo);
            await seed.SaveChangesAsync();
            itemId = item.Id;
            photoId = photo.Id;
        }

        TenantContext ownerTenant = new();
        ownerTenant.SetOrganization(owner);
        await using HaulScoutDbContext ownerDb = new(options, ownerTenant);
        var ownerPhotos = Workflow(ownerDb, owner, new DatabaseInventoryPhotoBlobStore());
        Assert.Equal(new byte[] { 4, 5, 6 }, (await ownerPhotos.GetThumbnailAsync(itemId, photoId, CancellationToken.None))?.Bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await ownerPhotos.GetOriginalAsync(itemId, photoId, CancellationToken.None))?.Bytes);

        TenantContext foreignTenant = new();
        foreignTenant.SetOrganization(other);
        await using HaulScoutDbContext foreignDb = new(options, foreignTenant);
        var foreignPhotos = Workflow(foreignDb, other, new DatabaseInventoryPhotoBlobStore());
        Assert.Null(await foreignPhotos.GetThumbnailAsync(itemId, photoId, CancellationToken.None));
        Assert.Null(await foreignPhotos.GetOriginalAsync(itemId, photoId, CancellationToken.None));
    }

    [Fact]
    public async Task ScopedPhotoStoredInObjectStorageIsServedToItsOwner()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(ScopedPhotoStoredInObjectStorageIsServedToItsOwner)).Options;
        Guid itemId;
        Guid photoId;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        await using (HaulScoutDbContext seed = new(options, tenant))
        {
            InventoryItem item = new() { OrganizationId = org, Title = "Object photo" };
            InventoryPhoto photo = new()
            {
                OrganizationId = org,
                InventoryItemId = item.Id,
                ContentType = "image/webp",
                OriginalObjectKey = "org/photo/original.webp",
                ThumbnailObjectKey = "org/photo/thumbnail.jpg",
            };
            seed.InventoryItems.Add(item);
            seed.InventoryPhotos.Add(photo);
            await seed.SaveChangesAsync();
            itemId = item.Id;
            photoId = photo.Id;
        }

        FakePhotoObjects objects = new();
        await objects.PutAsync("org/photo/original.webp", [7, 7, 7], "image/webp", CancellationToken.None);
        await objects.PutAsync("org/photo/thumbnail.jpg", [8, 8], "image/jpeg", CancellationToken.None);
        await using HaulScoutDbContext db = new(options, tenant);
        var blobs = new ObjectStoreInventoryPhotoBlobStore(objects,
            Options.Create(new PhotoObjectStorageOptions { Bucket = "photos" }),
            NullLogger<ObjectStoreInventoryPhotoBlobStore>.Instance);
        var photos = Workflow(db, org, blobs);
        Assert.Equal(new byte[] { 8, 8 }, (await photos.GetThumbnailAsync(itemId, photoId, CancellationToken.None))?.Bytes);
        Assert.Equal(new byte[] { 7, 7, 7 }, (await photos.GetOriginalAsync(itemId, photoId, CancellationToken.None))?.Bytes);
    }

    private static InventoryPhotoWorkflowService Workflow(HaulScoutDbContext db, Guid org, IInventoryPhotoBlobStore blobs) =>
        new(new EfInventoryPhotoStore(db), new KnownUser(org), null!, blobs, null!);

    private sealed class KnownUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => Guid.Empty;
        public Task ResolveAsync() => Task.CompletedTask;
    }
}
