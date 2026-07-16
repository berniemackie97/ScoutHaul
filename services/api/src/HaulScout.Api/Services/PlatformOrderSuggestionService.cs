using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Builds operator-facing inventory suggestions for unreconciled platform order lines.
/// Suggestions are advisory only; reconciliation still requires explicit operator confirmation.
/// </summary>
public sealed class PlatformOrderSuggestionService(HaulScoutDbContext db, ICurrentUserContext currentUser)
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

        var inventory = await db.InventoryItems
            .Where(item => item.OrganizationId == currentUser.OrganizationId && !item.IsArchived)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new { item.Id, item.Title, item.Status })
            .Take(2000)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, string> titleById = inventory.ToDictionary(item => item.Id, item => item.Title);
        Dictionary<Guid, bool> soldById = inventory.ToDictionary(item => item.Id, item => InventoryLifecycle.IsSold(item.Status));
        List<(Guid Id, string Title)> titleCandidates = inventory.Select(item => (item.Id, item.Title)).ToList();

        List<string> externalIds = openLines.Where(line => line.ExternalItemId is not null).Select(line => line.ExternalItemId!).Distinct().ToList();
        var byExternalId = externalIds.Count == 0
            ? []
            : await db.ChannelListings
                .Where(listing => listing.OrganizationId == currentUser.OrganizationId
                    && listing.ExternalListingId != null && externalIds.Contains(listing.ExternalListingId))
                .Select(listing => new { listing.ExternalListingId, listing.InventoryItemId })
                .ToListAsync(cancellationToken);

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
