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

    /// <summary>
    /// Deliberately bypasses the organization query filter. The photo endpoints serve bytes addressed by item and
    /// photo id, and the request's tenant is not always resolved on that path; the ids are unguessable and the row is
    /// only ever returned as image bytes. Preserved exactly as it was — narrowing it here would break photo serving.
    /// </summary>
    public Task<InventoryPhoto?> FindPhotoUnscopedAsync(Guid inventoryItemId, Guid photoId, CancellationToken cancellationToken) =>
        db.InventoryPhotos
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == photoId && item.InventoryItemId == inventoryItemId, cancellationToken);

    public async Task AddAsync(InventoryPhoto photo, CancellationToken cancellationToken) =>
        await db.InventoryPhotos.AddAsync(photo, cancellationToken);

    public void Remove(InventoryPhoto photo) => db.InventoryPhotos.Remove(photo);

    public Task LoadPhotosAsync(InventoryItem item, CancellationToken cancellationToken) =>
        db.Entry(item).Collection(existing => existing.Photos).LoadAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
