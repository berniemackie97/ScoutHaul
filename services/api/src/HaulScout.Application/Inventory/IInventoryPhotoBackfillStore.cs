using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Maintenance access to photo rows across organizations: moving database-held images and explicitly repairing
/// existing originals. Never exposed to an HTTP operator request.
/// </summary>
public interface IInventoryPhotoBackfillStore
{
    /// <summary>The oldest photos still holding their images, so the move works through the backlog in a stable order.</summary>
    Task<IReadOnlyList<InventoryPhoto>> ListHeldInDatabaseAsync(int take, CancellationToken cancellationToken);

    /// <summary>How many are left to move. Logged as progress, and it is what tells the move it is finished.</summary>
    Task<int> CountHeldInDatabaseAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-independent keyset scan of stored photos, ordered by identifier for manual metadata repair.</summary>
    Task<IReadOnlyList<InventoryPhoto>> ListForMetadataRepairAsync(Guid? afterPhotoId, int take, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
