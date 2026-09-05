using HaulScout.Domain;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Which items a caller wants. This exists because the query services used to compose EF <c>IQueryable</c> chains
/// directly: the filtering has to run as SQL, so it cannot simply be moved into the Application layer as LINQ-to-objects
/// without loading the table. Describing the intent as data instead lets the Application own <em>what</em> is being
/// asked for while Infrastructure owns <em>how</em> it becomes a query.
/// </summary>
/// <param name="Search">Free-text match over title/brand/model/category/barcode/location. Null means no filter.</param>
/// <param name="Ownership">Whose item it is. Null means every ownership type.</param>
/// <param name="NeedsBenchWork">
/// Only items waiting on repair, per <see cref="InventoryBenchWork"/>. False means no filter — it is
/// a switch rather than a nullable flag because "only items that need no bench work" is not a queue
/// anyone asks for.
/// </param>
public sealed record InventoryFilter(
    InventoryStatus? Status = null,
    InventoryLifecycleGroup? Lifecycle = null,
    bool IncludeArchived = false,
    string? Search = null,
    InventoryAttention? Attention = null,
    InventoryOwnershipType? Ownership = null,
    bool NeedsBenchWork = false);

/// <summary>The sortable columns, as a closed set. A string sort key from the wire is mapped onto this by the
/// Application, so an unknown value can never reach the database as a column name.</summary>
public enum InventorySortField
{
    UpdatedAt,
    Title,
    PurchasePrice,
    EstimatedSalePrice,
    CreatedAt,
}

/// <param name="Descending">Sort direction for <paramref name="Field"/>.</param>
public sealed record InventorySort(InventorySortField Field, bool Descending);

/// <summary>
/// The handful of figures the browse summary is computed from. Only these columns are read rather than whole entities,
/// because the summary spans every matching row and materializing them all to count them would be wasteful.
/// </summary>
public sealed record InventoryScalars(
    InventoryStatus Status,
    decimal? PurchasePrice,
    decimal? EstimatedSalePrice,
    decimal? SellerAskingPrice,
    InventoryFunctionalStatus FunctionalStatus = InventoryFunctionalStatus.Unknown,
    InventoryOwnershipType OwnershipType = InventoryOwnershipType.Owned);
