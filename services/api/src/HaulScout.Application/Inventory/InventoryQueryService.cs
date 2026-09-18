using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Reading inventory. The paging rules, the sort whitelist and the summary arithmetic are decisions, so they live here;
/// turning a filter into SQL is a mechanism, so it lives behind <see cref="IInventoryStore"/>.
/// </summary>
public sealed class InventoryQueryService(IInventoryStore inventory, ICurrentUserContext currentUser)
{
    private const int ListLimit = 200;
    private const int MaxPageSize = 100;

    public async Task<InventoryItemDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        InventoryItem? item = await inventory.FindDetailAsync(currentUser.OrganizationId, id, cancellationToken);
        return item?.ToDto();
    }

    public async Task<IReadOnlyList<InventoryItemDto>> ListAsync(
        InventoryStatus? status,
        InventoryLifecycleGroup? lifecycle,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        InventoryFilter filter = new(status, lifecycle, includeArchived);
        IReadOnlyList<InventoryItem> items = await inventory.ListAsync(
            currentUser.OrganizationId, filter, ListLimit, cancellationToken);

        return items.Select(item => item.ToDto()).ToArray();
    }

    public async Task<InventoryListDto> BrowseAsync(
        InventoryStatus? status,
        InventoryLifecycleGroup? lifecycle,
        bool includeArchived,
        string? search,
        InventoryAttention? attention,
        string? sort,
        bool desc,
        int page,
        int pageSize,
        InventoryOwnershipType? ownership,
        bool needsBenchWork,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        InventoryFilter filter = new(status, lifecycle, includeArchived, TrimToNull(search), attention, ownership, needsBenchWork);
        Guid organizationId = currentUser.OrganizationId;

        IReadOnlyList<InventoryScalars> scalars = await inventory.ListScalarsAsync(organizationId, filter, cancellationToken);
        InventorySummaryDto summary = Summarize(scalars);

        IReadOnlyList<InventoryItem> pageItems = await inventory.BrowseAsync(
            organizationId, filter, ToSort(sort, desc), (page - 1) * pageSize, pageSize, cancellationToken);

        int total = scalars.Count;
        int pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        return new InventoryListDto(
            pageItems.Select(item => item.ToDto()).ToList(), total, page, pageSize, pageCount, summary);
    }

    /// <summary>
    /// The browse summary for a filter, without fetching a page of items to go with it.
    ///
    /// For callers that show the figures and link to the list — the home console — so what they show
    /// is the same computation the list's own summary cards run.
    /// </summary>
    public async Task<InventorySummaryDto> SummaryAsync(InventoryFilter filter, CancellationToken cancellationToken) =>
        Summarize(await inventory.ListScalarsAsync(currentUser.OrganizationId, filter, cancellationToken));

    /// <summary>How many items match a filter — the browse list's own count, for anything that links to it.</summary>
    public Task<int> CountAsync(InventoryFilter filter, CancellationToken cancellationToken) =>
        inventory.CountAsync(currentUser.OrganizationId, filter, cancellationToken);

    /// <summary>The summary spans every match, not just the page being shown — so it is computed from the filtered
    /// set's figures rather than from <c>pageItems</c>.</summary>
    private static InventorySummaryDto Summarize(IReadOnlyList<InventoryScalars> scalars) => new(
        scalars.Count,
        scalars.Count(item => InventoryLifecycle.Group(item.Status) == InventoryLifecycleGroup.Active),
        scalars.Count(item => item.Status is InventoryStatus.ReadyToList or InventoryStatus.Listed),
        scalars.Count(item => InventoryLifecycle.IsSold(item.Status)),
        scalars.Sum(item => item.PurchasePrice ?? 0),
        scalars.Sum(item => item.EstimatedSalePrice ?? item.SellerAskingPrice ?? 0),
        scalars.Sum(item => item.SellerAskingPrice ?? 0),
        scalars.Count(item => InventoryBenchWork.IsQueued(item.FunctionalStatus, item.OwnershipType)),
        scalars.Count(item => item.OwnershipType == InventoryOwnershipType.CustomerRepair),
        scalars.Count(item => item.FunctionalStatus == InventoryFunctionalStatus.NonFunctional));

    /// <summary>An unrecognised sort key falls back to most-recently-updated rather than failing — and, because the
    /// result is a closed enum, a caller's string can never reach the database as a column name.</summary>
    private static InventorySort ToSort(string? sort, bool desc) => new(
        sort?.ToLowerInvariant() switch
        {
            "title" => InventorySortField.Title,
            "purchaseprice" => InventorySortField.PurchasePrice,
            "estimatedsaleprice" => InventorySortField.EstimatedSalePrice,
            "createdat" => InventorySortField.CreatedAt,
            _ => InventorySortField.UpdatedAt,
        },
        desc);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
