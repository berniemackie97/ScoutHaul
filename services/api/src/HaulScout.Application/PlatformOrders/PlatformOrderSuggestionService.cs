using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

/// <summary>
/// Builds operator-facing inventory suggestions for unreconciled platform order lines.
/// Suggestions are advisory only; reconciliation still requires explicit operator confirmation.
/// </summary>
public sealed class PlatformOrderSuggestionService(IPlatformOrderSuggestionStore store)
{
    public async Task<Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)>> BuildSuggestionsAsync(
        IReadOnlyList<PlatformOrder> orders,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, (Guid, string, bool)> suggestions = new();
        List<PlatformOrderLine> openLines = orders
            .Where(order => order.Status != PlatformOrderStatus.Reconciled)
            .SelectMany(order => order.Lines.Where(line => line.InventoryItemId is null))
            .ToList();
        if (openLines.Count == 0)
        {
            return suggestions;
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

            if (match is not null && titleById.TryGetValue(match.InventoryItemId, out string? title))
            {
                suggestions[line.Id] = (match.InventoryItemId, title, match.AlreadySold);
            }
        }

        return suggestions;
    }
}

public sealed record PlatformOrderInventorySuggestionCandidate(Guid Id, string Title, string? Sku, InventoryStatus Status);

public sealed record PlatformOrderListingSuggestionCandidate(string? ExternalListingId, Guid InventoryItemId);
