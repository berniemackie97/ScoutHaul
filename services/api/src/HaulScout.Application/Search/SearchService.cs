using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;

namespace HaulScout.Application.Search;

/// <summary>
/// One question — "where is this thing?" — across everything the operator can see.
///
/// The portal had per-page search on two of twenty-eight screens, so finding an order meant already
/// knowing it was an order, going to Orders, and searching there.
///
/// Not gated by a single permission: this reads across inventory, orders, customers and purchases,
/// and a caller who may see only one of those should get that one rather than all four or a refusal.
/// Each section is asked for on its own and simply left out when it is not allowed — a search that
/// returns records someone cannot open is a leak, not a convenience.
/// </summary>
public sealed class SearchService(ISearchStore store, ICurrentUserContext currentUser, IOrganizationAuthorizer access)
{
    /// <summary>Per group. Enough to recognise the one you meant; not enough to become a list page.</summary>
    public const int PerGroupLimit = 6;

    /// <summary>
    /// Below this a term matches most of the business and none of it usefully. Barcodes and short
    /// order numbers are why it is 2 rather than 3.
    /// </summary>
    public const int MinimumQueryLength = 2;

    public async Task<SearchResultsDto> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        string term = (query ?? string.Empty).Trim();

        // Not an error: an empty palette is the normal state before anyone has typed anything.
        if (term.Length < MinimumQueryLength || currentUser.ActiveOrganizationId is not Guid organizationId)
        {
            return new SearchResultsDto(term, [], 0);
        }

        List<SearchGroupDto> groups = [];

        if (await access.HasPermission(organizationId, HaulScoutPermissions.ManageInventory, cancellationToken))
        {
            groups.Add(Group("inventory", "Inventory",
                await store.SearchInventoryAsync(organizationId, term, PerGroupLimit + 1, cancellationToken)));
        }

        if (await access.HasPermission(organizationId, HaulScoutPermissions.ManageOrders, cancellationToken))
        {
            groups.Add(Group("order", "Orders",
                await store.SearchOrdersAsync(organizationId, term, PerGroupLimit + 1, cancellationToken)));
            groups.Add(Group("customer", "Customers",
                await store.SearchCustomersAsync(organizationId, term, PerGroupLimit + 1, cancellationToken)));
        }

        if (await access.HasPermission(organizationId, HaulScoutPermissions.ManageFinance, cancellationToken))
        {
            groups.Add(Group("purchase", "Purchases",
                await store.SearchPurchasesAsync(organizationId, term, PerGroupLimit + 1, cancellationToken)));
        }

        List<SearchGroupDto> populated = groups.Where(group => group.Hits.Count > 0).ToList();
        return new SearchResultsDto(term, populated, populated.Sum(group => group.Hits.Count));
    }

    /// <summary>
    /// Trims the over-fetched row back off and reports that it was there.
    ///
    /// One more than the limit is asked for so the caller can be told there is more behind the cut,
    /// rather than being shown six results that look like all six there are.
    /// </summary>
    private static SearchGroupDto Group(string kind, string label, IReadOnlyList<SearchRecord> matches) => new(
        kind,
        label,
        matches.Take(PerGroupLimit)
            .Select(record => new SearchHitDto(kind, record.Id, record.Label, record.Sublabel, record.Badge))
            .ToList(),
        matches.Count > PerGroupLimit);
}
