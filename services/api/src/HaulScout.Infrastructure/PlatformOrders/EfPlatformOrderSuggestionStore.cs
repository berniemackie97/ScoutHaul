using Microsoft.EntityFrameworkCore;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.PlatformOrders;

public sealed class EfPlatformOrderSuggestionStore(HaulScoutDbContext db, ICurrentUserContext currentUser) : IPlatformOrderSuggestionStore
{
    /// <summary>
    /// Inventory items as match targets. Barcode is supplied as the item's identifier code: an inventory
    /// item has no separate SKU column, and a seller's platform custom-label commonly carries the
    /// barcode. It is compared exactly, and a code shared by two items is rejected as ambiguous rather
    /// than guessed at.
    /// </summary>
    public async Task<IReadOnlyList<PlatformOrderInventorySuggestionCandidate>> ListInventoryCandidatesAsync(CancellationToken cancellationToken) =>
        await db.InventoryItems
            .Where(item => item.OrganizationId == currentUser.OrganizationId && !item.IsArchived)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new PlatformOrderInventorySuggestionCandidate(item.Id, item.Title, item.Barcode, item.Status))
            .Take(2000)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Which inventory item an order line's platform item number points at.
    ///
    /// Two independent records can answer this and both are consulted, because relying on only one
    /// was why order reconciliation depended on having reconciled the listing import first:
    ///   • <see cref="ChannelListing"/> — the manual channel-presence ledger.
    ///   • <see cref="PlatformListing"/> — an imported listing the operator already reconciled.
    /// A listings report and an orders report can be imported in either order, or only one of them
    /// imported at all, and the line still resolves if the answer exists anywhere.
    /// </summary>
    public async Task<IReadOnlyList<PlatformOrderListingSuggestionCandidate>> ListListingCandidatesAsync(
        IReadOnlyList<string> externalListingIds,
        CancellationToken cancellationToken)
    {
        List<PlatformOrderListingSuggestionCandidate> fromChannelListings = await db.ChannelListings
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId
                && listing.ExternalListingId != null
                && externalListingIds.Contains(listing.ExternalListingId))
            .Select(listing => new PlatformOrderListingSuggestionCandidate(
                listing.ChannelAccount!.Channel, listing.ExternalListingId, listing.InventoryItemId))
            .ToListAsync(cancellationToken);

        List<PlatformOrderListingSuggestionCandidate> fromImportedListings = await db.PlatformListings
            .Where(listing => listing.OrganizationId == currentUser.OrganizationId
                && listing.ReconciledInventoryItemId != null
                && externalListingIds.Contains(listing.ExternalListingId))
            .Select(listing => new PlatformOrderListingSuggestionCandidate(
                listing.Platform, listing.ExternalListingId, listing.ReconciledInventoryItemId!.Value))
            .ToListAsync(cancellationToken);

        // Channel listings first: an explicit channel record is the more deliberate statement of
        // where an item is listed, so it wins when both know the same external id.
        return [.. fromChannelListings, .. fromImportedListings];
    }
}
