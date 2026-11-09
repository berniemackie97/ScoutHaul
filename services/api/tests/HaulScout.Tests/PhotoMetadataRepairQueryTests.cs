using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PhotoMetadataRepairQueryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task KeysetScanIsOrderedAcrossOrganizationsAndResumesByPhotoId()
    {
        Guid organizationId = Guid.NewGuid();
        Guid before = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffff00");
        Guid first = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffff10");
        Guid second = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffff20");
        await using (HaulScoutDbContext seed = postgres.NewContext(organizationId))
        {
            seed.Organizations.Add(new Organization { Id = organizationId, Name = "Photo repair query" });
            InventoryItem item = new() { OrganizationId = organizationId, Title = "Legacy photo test" };
            seed.InventoryItems.Add(item);
            seed.InventoryPhotos.AddRange(
                new InventoryPhoto { Id = second, OrganizationId = organizationId, InventoryItemId = item.Id,
                    ContentType = "image/jpeg", OriginalBytes = [2], ThumbnailBytes = [2] },
                new InventoryPhoto { Id = first, OrganizationId = organizationId, InventoryItemId = item.Id,
                    ContentType = "image/jpeg", OriginalBytes = [1], ThumbnailBytes = [1] });
            await seed.SaveChangesAsync();
        }

        await using HaulScoutDbContext db = postgres.NewContext(Guid.NewGuid());
        EfInventoryPhotoBackfillStore store = new(db);
        var page = await store.ListForMetadataRepairAsync(before, 1, CancellationToken.None);
        Assert.Equal(first, Assert.Single(page).Id);
        var next = await store.ListForMetadataRepairAsync(first, 1, CancellationToken.None);
        Assert.Equal(second, Assert.Single(next).Id);
        Assert.Empty(await store.ListForMetadataRepairAsync(second, 1, CancellationToken.None));
    }
}
