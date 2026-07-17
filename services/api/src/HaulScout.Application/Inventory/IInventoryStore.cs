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

    /// <summary>
    /// Whether a sale still holds this item — i.e. one that has not released it back (returned/refunded/cancelled).
    /// Inventory has to ask because an item under an active sale may only change stage through the sales workflow;
    /// the question is answered here rather than by handing the use case a Sales query.
    /// </summary>
    Task<bool> HasActiveSaleAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken);

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

    /// <summary>
    /// The candidate components for a bundle, each with its sales loaded — an item still held by a live sale cannot be
    /// bundled, and that has to be decided from the item's sales rather than from its status alone.
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> ListBundleCandidatesAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    Task AddAsync(InventoryItem item, CancellationToken cancellationToken);

    Task AddBundleComponentAsync(InventoryBundleComponent component, CancellationToken cancellationToken);

    /// <summary>Loads a bundle's components after it has been written, so the caller can return the whole thing.</summary>
    Task LoadBundleComponentsAsync(InventoryItem bundle, CancellationToken cancellationToken);

    /// <summary>The item with its bundle components and their items — what removal needs in order to release them.</summary>
    Task<InventoryItem?> FindWithBundleComponentsAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    /// <summary>Everything still pointing at this item, gathered in one call. See <see cref="InventoryReferences"/>.</summary>
    Task<InventoryReferences> InspectReferencesAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Destroys the item along with the rows that only exist because of it, and detaches the ones that outlive it.
    /// Which dependents are owned and which are merely linked is a persistence fact, so the decision lives with the
    /// implementation rather than being restated by the use case.
    /// </summary>
    Task RemoveWithDependentsAsync(InventoryItem item, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
