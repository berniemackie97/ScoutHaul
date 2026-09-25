using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// The photos whose images are still held in the database after object storage has been configured. Asked across
/// every organization, because moving them is the service's own housekeeping rather than any one operator's request.
/// </summary>
public interface IInventoryPhotoBackfillStore
{
    /// <summary>The oldest photos still holding their images, so the move works through the backlog in a stable order.</summary>
    Task<IReadOnlyList<InventoryPhoto>> ListHeldInDatabaseAsync(int take, CancellationToken cancellationToken);

    /// <summary>How many are left to move. Logged as progress, and it is what tells the move it is finished.</summary>
    Task<int> CountHeldInDatabaseAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
