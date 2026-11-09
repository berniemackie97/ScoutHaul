using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// Maintenance reads span every organization, independently of the operator's tenant filter. The storage move
/// selects database-held photos; the metadata repair command keyset-scans all photo rows.
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

    public async Task<IReadOnlyList<InventoryPhoto>> ListForMetadataRepairAsync(Guid? afterPhotoId, int take, CancellationToken cancellationToken)
    {
        IQueryable<InventoryPhoto> photos = afterPhotoId is Guid cursor
            ? db.InventoryPhotos.FromSqlInterpolated($@"SELECT * FROM ""InventoryPhotos"" WHERE ""Id"" > {cursor}")
            : db.InventoryPhotos;

        return await photos.IgnoreQueryFilters()
            .OrderBy(photo => photo.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private IQueryable<InventoryPhoto> HeldInDatabase() =>
        db.InventoryPhotos
            .IgnoreQueryFilters()
            .Where(photo => photo.OriginalObjectKey == null && photo.OriginalBytes != null);
}
