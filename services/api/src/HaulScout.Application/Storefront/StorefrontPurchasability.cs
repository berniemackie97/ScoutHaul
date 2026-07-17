using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>
/// Whether a shopper can buy a listing right now — one rule, asked at add-to-cart, at cart read, and at checkout.
///
/// Both halves matter. The listing must be live (Active — not Reserved by someone's checkout, not Sold, not pulled).
/// The backing item must still be genuinely sellable, because nothing retires a store listing automatically when its
/// item sells on another channel: without the item half, the storefront would happily sell what left the shelf via
/// eBay an hour ago.
/// </summary>
public static class StorefrontPurchasability
{
    public static bool IsPurchasable(StoreListing? listing) =>
        listing?.Status == StoreListingStatus.Active && IsItemSellable(listing.InventoryItem);

    /// <summary>The shelf rule: present, not archived, and Listed — StorePublishing.IsOnShelf, null-tolerant.</summary>
    public static bool IsItemSellable(InventoryItem? item) =>
        item is not null && StorePublishing.IsOnShelf(item);

    /// <summary>Why an add was refused, phrased for the shopper.</summary>
    public static string RefusalReason(StoreListing listing) =>
        listing.Status == StoreListingStatus.Reserved
            ? "This item is in someone else's checkout right now. If they don't complete it, it will come back shortly."
            : "This item is no longer available.";
}
