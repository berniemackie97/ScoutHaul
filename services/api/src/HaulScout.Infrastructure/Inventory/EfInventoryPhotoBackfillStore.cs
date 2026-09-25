using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// Photos still holding their images in their own columns. The filters are ignored deliberately: this runs for the
/// whole service rather than inside one operator's request, and every organization's photos are moved the same way.
/// </summary>
public sealed class EfInventoryPhotoBackfillStore(HaulScoutDbContext db) : IInventoryPhotoBackfillStore
{
    public async Task<IReadOnlyList<InventoryPhoto>> ListHeldInDatabaseAsync(int take, CancellationToken cancellationToken) =>
        await HeldInDatabase()
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountHeldInDatabaseAsync(CancellationToken cancellationToken) =>
        HeldInDatabase().CountAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private IQueryable<InventoryPhoto> HeldInDatabase() =>
        db.InventoryPhotos
            .IgnoreQueryFilters()
            .Where(photo => photo.OriginalObjectKey == null && photo.OriginalBytes != null);
}
