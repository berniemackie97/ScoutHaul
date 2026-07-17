using Microsoft.EntityFrameworkCore;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.PlatformOrders;

public sealed class EfPlatformOrderSuggestionStore(HaulScoutDbContext db, ICurrentUserContext currentUser) : IPlatformOrderSuggestionStore
{
    public async Task<IReadOnlyList<PlatformOrderInventorySuggestionCandidate>> ListInventoryCandidatesAsync(CancellationToken cancellationToken) =>
        await db.InventoryItems
            .Where(item => item.OrganizationId == currentUser.OrganizationId && !item.IsArchived)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new PlatformOrderInventorySuggestionCandidate(item.Id, item.Title, item.Status))
            .Take(2000)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlatformOrderListingSuggestionCandidate>> ListListingCandidatesAsync(
        IReadOnlyList<string> externalListingIds,
        CancellationToken cancellationToken) =>
        await db.ChannelListings
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId
                && listing.ExternalListingId != null
                && externalListingIds.Contains(listing.ExternalListingId))
            .Select(listing => new PlatformOrderListingSuggestionCandidate(listing.ExternalListingId, listing.InventoryItemId))
            .ToListAsync(cancellationToken);
}
