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
        Dictionary<Guid, bool> soldById = inventory.ToDictionary(item => item.Id, item => InventoryLifecycle.IsSold(item.Status));
        List<(Guid Id, string Title)> titleCandidates = inventory.Select(item => (item.Id, item.Title)).ToList();

        List<string> externalIds = openLines.Where(line => line.ExternalItemId is not null).Select(line => line.ExternalItemId!).Distinct().ToList();
        var byExternalId = externalIds.Count == 0
            ? []
            : await store.ListListingCandidatesAsync(externalIds, cancellationToken);

        foreach (PlatformOrderLine line in openLines)
        {
            Guid? matchId = line.ExternalItemId is null
                ? null
                : byExternalId.FirstOrDefault(candidate => candidate.ExternalListingId == line.ExternalItemId)?.InventoryItemId;
            matchId ??= TitleMatch.Best(line.Title, titleCandidates);
            if (matchId is Guid id && titleById.TryGetValue(id, out string? title))
            {
                suggestions[line.Id] = (id, title, soldById.GetValueOrDefault(id));
            }
        }

        return suggestions;
    }
}

public sealed record PlatformOrderInventorySuggestionCandidate(Guid Id, string Title, InventoryStatus Status);

public sealed record PlatformOrderListingSuggestionCandidate(string? ExternalListingId, Guid InventoryItemId);
