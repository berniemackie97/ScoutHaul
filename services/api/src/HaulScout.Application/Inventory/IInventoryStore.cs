using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Inventory's persistence port. The Application states what it needs about inventory items; Infrastructure decides how
/// that becomes SQL. Every method is named for the caller's intent rather than for a query, so the use cases read as
/// business steps and no EF concept (IQueryable, Include, change tracking) leaks across the boundary.
/// </summary>
public interface IInventoryStore
{
    /// <summary>The full item as the detail screens need it — photos, issues, lots and bundle components loaded.</summary>
    Task<InventoryItem?> FindDetailAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    /// <summary>The bare row, for callers that only intend to change it.</summary>
    Task<InventoryItem?> FindAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryItem>> ListAsync(
        Guid organizationId,
        InventoryFilter filter,
        int take,
        CancellationToken cancellationToken);

    /// <summary>One page of matches, sorted and hydrated for display.</summary>
    Task<IReadOnlyList<InventoryItem>> BrowseAsync(
        Guid organizationId,
        InventoryFilter filter,
        InventorySort sort,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>Every match's summary figures — the whole filtered set, not just the page it is shown with.</summary>
    Task<IReadOnlyList<InventoryScalars>> ListScalarsAsync(
        Guid organizationId,
        InventoryFilter filter,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
