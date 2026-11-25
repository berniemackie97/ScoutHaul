using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>Shows where an item's linked listings disagree with it, and takes a listing's value when a person chooses to.</summary>
public sealed class InventoryListingDifferencesService(
    IInventoryListingDifferenceStore store,
    IAuditTrail audit,
    ICurrentUserContext currentUser)
{
    public async Task<AppResult<IReadOnlyList<ListingDifferenceDto>>> ListAsync(Guid itemId, CancellationToken cancellationToken)
    {
        InventoryItem? item = await store.FindItemAsync(currentUser.OrganizationId, itemId, cancellationToken);
        if (item is null)
        {
            return AppResult<IReadOnlyList<ListingDifferenceDto>>.NotFound("Inventory item not found.");
        }

        IReadOnlyList<PlatformListing> listings = await store.ListLinkedListingsAsync(currentUser.OrganizationId, itemId, cancellationToken);
        return AppResult<IReadOnlyList<ListingDifferenceDto>>.Ok(Differences(item, listings));
    }

    public async Task<AppResult<IReadOnlyList<ListingDifferenceDto>>> ApplyAsync(
        Guid itemId,
        ApplyListingDifferenceRequest request,
        CancellationToken cancellationToken)
    {
        InventoryItem? item = await store.FindItemAsync(currentUser.OrganizationId, itemId, cancellationToken);
        if (item is null)
        {
            return AppResult<IReadOnlyList<ListingDifferenceDto>>.NotFound("Inventory item not found.");
        }

        IReadOnlyList<PlatformListing> listings = await store.ListLinkedListingsAsync(currentUser.OrganizationId, itemId, cancellationToken);
        PlatformListing? listing = listings.FirstOrDefault(entry => entry.Id == request.ListingId);
        if (listing is null)
        {
            return AppResult<IReadOnlyList<ListingDifferenceDto>>.NotFound("That listing is no longer linked to this item.");
        }

        ListingDetailDifference? difference = ListingDetailDifferences.Find(item, listing).FirstOrDefault(entry => entry.Field == request.Field);
        if (difference is null || !ListingDetailDifferences.Apply(item, listing, request.Field))
        {
            return AppResult<IReadOnlyList<ListingDifferenceDto>>.Conflict("The listing and the item already agree on that detail.");
        }

        await audit.RecordAsync(
            nameof(InventoryItem),
            item.Id,
            "ListingDetailTaken",
            new { listing.Platform, ListingId = listing.Id, Field = request.Field.ToString(), From = difference.Current, To = difference.Incoming },
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<IReadOnlyList<ListingDifferenceDto>>.Ok(Differences(item, listings));
    }

    private static ListingDifferenceDto[] Differences(InventoryItem item, IReadOnlyList<PlatformListing> listings) =>
    [
        .. listings.SelectMany(listing => ListingDetailDifferences.Find(item, listing).Select(difference => new ListingDifferenceDto(
            listing.Id,
            listing.Platform,
            listing.Url,
            difference.Field,
            difference.Current,
            difference.Incoming,
            difference.Raw))),
    ];
}
