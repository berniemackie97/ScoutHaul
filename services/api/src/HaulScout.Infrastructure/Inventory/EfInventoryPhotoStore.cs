using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

public sealed class EfInventoryPhotoStore(HaulScoutDbContext db) : IInventoryPhotoStore
{
    public Task<InventoryItem?> FindItemWithPhotosAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems
            .Include(item => item.Photos)
            .Include(item => item.Issues)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
                .ThenInclude(item => item.ComponentInventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == organizationId, cancellationToken);

    public Task<InventoryPhoto?> FindPhotoForOrganizationAsync(Guid organizationId, Guid inventoryItemId, Guid photoId, CancellationToken cancellationToken) =>
        db.InventoryPhotos
            .SingleOrDefaultAsync(photo =>
                photo.Id == photoId
                && photo.InventoryItemId == inventoryItemId
                && photo.OrganizationId == organizationId
                && photo.InventoryItem!.OrganizationId == organizationId,
                cancellationToken);

    public async Task AddAsync(InventoryPhoto photo, CancellationToken cancellationToken) =>
        await db.InventoryPhotos.AddAsync(photo, cancellationToken);

    public void Remove(InventoryPhoto photo) => db.InventoryPhotos.Remove(photo);

    public Task LoadPhotosAsync(InventoryItem item, CancellationToken cancellationToken) =>
        db.Entry(item).Collection(existing => existing.Photos).LoadAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
