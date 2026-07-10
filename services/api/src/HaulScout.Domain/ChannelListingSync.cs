using HaulScout.Domain.Entities;

namespace HaulScout.Domain;

/// <summary>
/// Cross-channel effect of a sale: the channel it sold on is marked Sold; every other channel where it is
/// still live is flagged PendingDelist so the operator takes it down. Pure mutation over loaded entities —
/// callers persist. Human-led: this only records intent; nothing is auto-removed from any marketplace.
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
}
