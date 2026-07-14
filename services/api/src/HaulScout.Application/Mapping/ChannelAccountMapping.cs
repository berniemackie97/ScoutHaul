using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Mapping;

public static class ChannelAccountMapping
{
    public static ChannelAccountDto ToDto(this ChannelAccount item) => new(
        item.Id,
        item.Channel,
        item.DisplayName,
        item.ExternalAccountId,
        item.DefaultFeeProfileId,
        item.IsNativelySynced,
        item.IsActive,
        item.ConnectedAt,
        item.Notes,
        item.Listings.Count(listing => listing.Status == ChannelListingStatus.Active));
}
