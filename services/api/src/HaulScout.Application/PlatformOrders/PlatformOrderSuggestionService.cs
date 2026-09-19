using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

/// <summary>
/// The item a line most likely is, and how sure that is. <see cref="Exact"/> means an identifier
/// matched — the platform's item number or the SKU; otherwise it is a title resemblance, a guess a
/// person must confirm, never one to pre-select.
/// </summary>
public sealed record PlatformLineSuggestion(Guid Id, string Title, bool AlreadySold, bool Exact);

/// <summary>
/// Builds operator-facing inventory suggestions for unreconciled platform order lines.
/// Suggestions are advisory only; reconciliation still requires explicit operator confirmation.
/// </summary>
public sealed class PlatformOrderSuggestionService(IPlatformOrderSuggestionStore store)
{
    /// <summary>
    /// The operator-facing suggestion per open line. Advisory: reconciliation still needs a person,
    /// except where <see cref="BuildMatchesAsync"/> reports a match safe to settle on its own.
    /// </summary>
    public async Task<Dictionary<Guid, PlatformLineSuggestion>> BuildSuggestionsAsync(
        IReadOnlyList<PlatformOrder> orders,
        CancellationToken cancellationToken)
    {
        (Dictionary<Guid, PlatformLineMatch> matches, Dictionary<Guid, string> titleById) =
            await MatchOpenLinesAsync(orders, cancellationToken);

        return matches
            .Where(entry => titleById.ContainsKey(entry.Value.InventoryItemId))
            .ToDictionary(
                entry => entry.Key,
                entry => new PlatformLineSuggestion(
                    entry.Value.InventoryItemId, titleById[entry.Value.InventoryItemId], entry.Value.AlreadySold, entry.Value.IsExact));
    }

    /// <summary>
    /// The full match per open line, including how it was found. Callers deciding whether to settle
    /// without a person need the source, not just the item — see <see cref="PlatformLineMatch.CanAutoSettle"/>.
    /// </summary>
    public async Task<Dictionary<Guid, PlatformLineMatch>> BuildMatchesAsync(
        IReadOnlyList<PlatformOrder> orders,
        CancellationToken cancellationToken)
    {
        (Dictionary<Guid, PlatformLineMatch> matches, _) = await MatchOpenLinesAsync(orders, cancellationToken);
        return matches;
    }

    private async Task<(Dictionary<Guid, PlatformLineMatch> Matches, Dictionary<Guid, string> TitleById)> MatchOpenLinesAsync(
        IReadOnlyList<PlatformOrder> orders,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, PlatformLineMatch> suggestions = new();
        List<PlatformOrderLine> openLines = orders
            .Where(order => order.Status != PlatformOrderStatus.Reconciled)
            .SelectMany(order => order.Lines.Where(line => line.InventoryItemId is null))
            .ToList();
        if (openLines.Count == 0)
        {
            return (suggestions, []);
        }

        IReadOnlyList<PlatformOrderInventorySuggestionCandidate> inventory =
            await store.ListInventoryCandidatesAsync(cancellationToken);
        Dictionary<Guid, string> titleById = inventory.ToDictionary(item => item.Id, item => item.Title);
        List<InventoryMatchCandidate> candidates = inventory
            .Select(item => new InventoryMatchCandidate(item.Id, item.Title, item.Sku, InventoryLifecycle.IsSold(item.Status)))
            .ToList();

        List<string> externalIds = openLines.Where(line => line.ExternalItemId is not null).Select(line => line.ExternalItemId!).Distinct().ToList();
        IReadOnlyList<PlatformOrderListingSuggestionCandidate> listingLinks = externalIds.Count == 0
            ? []
            : await store.ListListingCandidatesAsync(externalIds, cancellationToken);

        // First link wins per item id — the store returns channel listings ahead of imported ones.
        Dictionary<string, Guid> itemIdToInventoryId = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlatformOrderListingSuggestionCandidate link in listingLinks)
        {
            if (link.ExternalListingId is string externalId)
            {
                itemIdToInventoryId.TryAdd(externalId, link.InventoryItemId);
            }
        }

        foreach (PlatformOrderLine line in openLines)
        {
            PlatformLineMatch? match = PlatformLineMatcher.Match(
                new PlatformLineIdentity(line.ExternalItemId, line.Sku, line.Title),
                itemIdToInventoryId,
                candidates);

            if (match is not null)
            {
                suggestions[line.Id] = match;
            }
        }

        return (suggestions, titleById);
    }
}

public sealed record PlatformOrderInventorySuggestionCandidate(Guid Id, string Title, string? Sku, InventoryStatus Status);

public sealed record PlatformOrderListingSuggestionCandidate(string? ExternalListingId, Guid InventoryItemId);
