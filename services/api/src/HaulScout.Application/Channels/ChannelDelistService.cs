using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Channels;

/// <summary>
/// What is still for sale after it has already been sold.
///
/// Recording a sale marks every other marketplace's live listing as waiting to come down; until someone takes it
/// down, that listing can be bought again, and the second buyer is owed an apology and a refund. The rule that
/// creates the flag has existed for a while and nothing ever showed the operator the pile it makes.
///
/// Taking a listing down is the operator's own act, on the marketplace. This says which ones, gives them the link,
/// and records the answer.
/// </summary>
public sealed class ChannelDelistService(IChannelDelistStore store, ICurrentUserContext currentUser, TimeProvider clock)
{
    private const int MostToShow = 200;

    public async Task<IReadOnlyList<PendingDelistDto>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ChannelListing> pending = await store.ListPendingAsync(currentUser.OrganizationId, MostToShow, cancellationToken);
        if (pending.Count == 0)
        {
            return [];
        }

        IReadOnlyDictionary<Guid, ItemSale> sales = await store.SoldOnAsync(
            currentUser.OrganizationId,
            pending.Select(listing => listing.InventoryItemId).Distinct().ToArray(),
            cancellationToken);

        return pending
            .Select(listing =>
            {
                ItemSale sale = sales.TryGetValue(listing.InventoryItemId, out ItemSale? found) ? found : new ItemSale(null, null);
                return new PendingDelistDto(
                    listing.Id,
                    listing.InventoryItemId,
                    listing.InventoryItem?.Title ?? "Untitled item",
                    listing.ChannelAccount?.Channel ?? "a marketplace",
                    listing.ChannelAccount?.DisplayName,
                    listing.ExternalListingId,
                    listing.ExternalUrl,
                    listing.ListedPrice,
                    sale.Channel,
                    sale.SoldAt);
            })
            .ToArray();
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        store.CountPendingAsync(currentUser.OrganizationId, cancellationToken);

    /// <summary>The operator has taken the listing down on the marketplace.</summary>
    public Task<AppResult<PendingDelistDto>> MarkDelistedAsync(Guid channelListingId, CancellationToken cancellationToken) =>
        SettleAsync(channelListingId, ChannelListingStatus.Delisted, cancellationToken);

    /// <summary>
    /// The listing should stay up: the sale it was flagged for did not take this item after all, or it was
    /// reversed. Putting it back to Active is what stops the queue asking again.
    /// </summary>
    public Task<AppResult<PendingDelistDto>> KeepListedAsync(Guid channelListingId, CancellationToken cancellationToken) =>
        SettleAsync(channelListingId, ChannelListingStatus.Active, cancellationToken);

    private async Task<AppResult<PendingDelistDto>> SettleAsync(
        Guid channelListingId,
        ChannelListingStatus status,
        CancellationToken cancellationToken)
    {
        ChannelListing? listing = await store.FindAsync(currentUser.OrganizationId, channelListingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<PendingDelistDto>.NotFound("That listing was not found.");
        }

        if (listing.Status != ChannelListingStatus.PendingDelist)
        {
            // Someone else answered it. Saying so beats overwriting their answer.
            return AppResult<PendingDelistDto>.Validation("That listing has already been dealt with. Reload to see where it stands.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        listing.Status = status;
        listing.LastSyncedAt = now;

        await store.SaveChangesAsync(cancellationToken);
        return AppResult<PendingDelistDto>.Ok(new PendingDelistDto(
            listing.Id,
            listing.InventoryItemId,
            listing.InventoryItem?.Title ?? "Untitled item",
            listing.ChannelAccount?.Channel ?? "a marketplace",
            listing.ChannelAccount?.DisplayName,
            listing.ExternalListingId,
            listing.ExternalUrl,
            listing.ListedPrice,
            null,
            null));
    }
}
