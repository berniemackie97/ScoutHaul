using HaulScout.Domain.Entities;

namespace HaulScout.Domain;

/// <summary>
/// Channel-presence effect of a sale: the channel it sold on is marked Sold; every other channel where it is
/// still live is flagged PendingDelist so the operator reviews and removes it manually. Pure mutation over
/// loaded entities — callers persist. Human-led: this only records intent; nothing is auto-removed from any marketplace.
/// </summary>
public static class ChannelListingSync
{
    public static void ApplySale(IReadOnlyCollection<ChannelListing> listings, string? soldChannel, DateTimeOffset now)
    {
        foreach (ChannelListing listing in listings)
        {
            bool isSoldChannel = !string.IsNullOrWhiteSpace(soldChannel)
                && string.Equals(listing.ChannelAccount?.Channel, soldChannel, StringComparison.OrdinalIgnoreCase);

            if (isSoldChannel && listing.Status is ChannelListingStatus.Active or ChannelListingStatus.PendingDelist)
            {
                listing.Status = ChannelListingStatus.Sold;
                listing.SoldAt ??= now;
                listing.LastSyncedAt = now;
            }
            else if (listing.Status == ChannelListingStatus.Active)
            {
                listing.Status = ChannelListingStatus.PendingDelist;
                listing.LastSyncedAt = now;
            }
        }
    }

    /// <summary>
    /// What the marketplace itself says about a listing, carried onto its presence on that channel: the price and
    /// quantity as listed, and the status where the marketplace is the authority. A listing that ended there is
    /// delisted here; one that came back is active again. A presence waiting on a person to take it down stays
    /// waiting while the listing is still live, and a sold presence never reverts.
    /// </summary>
    public static void ApplyPlatformListing(ChannelListing channel, PlatformListing listing, DateTimeOffset now)
    {
        channel.ListedPrice = listing.Price;
        channel.Quantity = listing.Quantity;
        channel.ExternalUrl = listing.Url ?? channel.ExternalUrl;
        channel.LastSyncedAt = now;

        switch (listing.Status)
        {
            case PlatformListingStatus.Active:
                if (channel.Status is ChannelListingStatus.Draft or ChannelListingStatus.Delisted or ChannelListingStatus.NotListed or ChannelListingStatus.Error)
                {
                    channel.Status = ChannelListingStatus.Active;
                }

                break;
            case PlatformListingStatus.Sold:
                if (channel.Status is ChannelListingStatus.Active or ChannelListingStatus.PendingDelist or ChannelListingStatus.Draft)
                {
                    channel.Status = ChannelListingStatus.Sold;
                    channel.SoldAt ??= now;
                }

                break;
            case PlatformListingStatus.Ended or PlatformListingStatus.Unsold or PlatformListingStatus.OutOfStock:
                if (channel.Status is ChannelListingStatus.Active or ChannelListingStatus.PendingDelist or ChannelListingStatus.Draft or ChannelListingStatus.Error)
                {
                    channel.Status = ChannelListingStatus.Delisted;
                }

                break;
        }
    }
}
