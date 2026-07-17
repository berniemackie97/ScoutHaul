using HaulScout.Domain.Entities;

namespace HaulScout.Domain;

/// <summary>
/// What puts an item on the storefront shelf, in one place.
///
/// <b>Listed is the switch.</b> Marking an item Listed puts it on the shelf; moving it out of Listed pulls it. The
/// <see cref="StoreListing"/> is the item's storefront record — created and retired automatically from the item's
/// lifecycle, while its presentation (title, price, category, copy) stays the operator's to edit in the admin.
///
/// Two different gates guard two different doors, and the distinction exists because imports are not operators:
///
/// <b>The listing gate</b> (<see cref="ReasonsItCannotBeListed"/>) fronts the MANUAL transition — an operator marking
/// an item Listed in the admin or the mobile app must have it fully shopper-ready, photos included. Imports do not
/// pass through this gate: a marketplace CSV reporting an item as listed is reality arriving, not a request, and
/// refusing it would make the books lie.
///
/// <b>The shelf gate</b> (<see cref="ReasonsItIsNotShoppable"/>) fronts the PUBLIC storefront. An imported item that
/// is Listed but has no photos exists as a Draft store listing — visible to the operator as "awaiting photos", never
/// to shoppers — and activates itself the moment its first photo lands. Nothing is silently dropped, and nothing
/// imageless is ever offered for sale.
/// </summary>
public static class StorePublishing
{
    /// <summary>An item is on the storefront shelf exactly when it is Listed and not archived.</summary>
    public static bool IsOnShelf(InventoryItem item) => !item.IsArchived && item.Status == InventoryStatus.Listed;

    /// <summary>The price a shopper pays: the operator's estimate, falling back to what the seller asked.</summary>
    public static decimal ShelfPrice(InventoryItem item) => item.EstimatedSalePrice ?? item.SellerAskingPrice ?? 0m;

    /// <summary>The item has left the building through a sale — its storefront record reads Sold, not hidden.</summary>
    public static bool HasLeftThroughSale(InventoryStatus status) =>
        status is InventoryStatus.Sold or InventoryStatus.Packed or InventoryStatus.Shipped or InventoryStatus.Delivered;

    /// <summary>
    /// Why an OPERATOR may not mark this item Listed yet, in the order they would fix it. Empty means go. All
    /// reasons are returned at once so the whole job is visible, not rediscovered one rejection at a time.
    /// </summary>
    public static IReadOnlyList<string> ReasonsItCannotBeListed(InventoryItem item)
    {
        List<string> reasons = [.. ReasonsItIsNotShoppable(item)];

        if (item.IsArchived)
        {
            reasons.Insert(0, "It is archived. Restore it before listing.");
        }

        return reasons;
    }

    /// <summary>
    /// Why a SHOPPER cannot be offered this item, independent of how it became Listed. The photo rule lives here —
    /// on the shelf gate, not the listing gate — so an imported, photo-less item is held as awaiting-photos rather
    /// than blocked from being Listed at all.
    /// </summary>
    public static IReadOnlyList<string> ReasonsItIsNotShoppable(InventoryItem item)
    {
        List<string> reasons = [];

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            reasons.Add("It needs a title — that is the name shoppers see.");
        }

        if (ShelfPrice(item) <= 0)
        {
            reasons.Add("It needs a price. Set an estimated sale price before it can be shown.");
        }

        if (item.Photos.Count == 0)
        {
            reasons.Add("It needs at least one photo. Nobody buys what they cannot see.");
        }

        // Selling someone else's property, or something we have reason to doubt, is not a presentation problem.
        if (item.OwnershipType == InventoryOwnershipType.CustomerRepair)
        {
            reasons.Add("It belongs to a repair customer and is not ours to sell.");
        }

        if (item.AuthenticityStatus == InventoryAuthenticityStatus.Counterfeit)
        {
            reasons.Add("It is marked counterfeit and cannot be sold.");
        }

        if (item.AuthenticityStatus == InventoryAuthenticityStatus.NeedsVerification)
        {
            reasons.Add("Its authenticity is unverified. Verify it before it can be shown.");
        }

        return reasons;
    }

    public static bool CanBeListed(InventoryItem item) => ReasonsItCannotBeListed(item).Count == 0;

    public static bool IsShoppable(InventoryItem item) => ReasonsItIsNotShoppable(item).Count == 0;

    /// <summary>
    /// The state this item's store listing should be in right now — the single rule the synchronizer applies
    /// wherever the item's lifecycle is written, so no code path can leave the shelf disagreeing with the inventory.
    ///
    /// Null means "no listing should be created" (the item has never been Listed and nothing needs retiring).
    /// A Reserved listing is NEVER redirected from here: Reserved means a shopper's checkout holds the item, and
    /// only the checkout's own state machine (payment, expiry, release) may resolve it — if the item was sold
    /// elsewhere mid-checkout, finalization refunds rather than double-sells.
    /// </summary>
    public static StoreListingStatus? DesiredListingStatus(InventoryItem item, StoreListingStatus? current)
    {
        if (current == StoreListingStatus.Reserved)
        {
            return StoreListingStatus.Reserved;
        }

        if (item.IsArchived)
        {
            return current is null ? null : StoreListingStatus.Archived;
        }

        if (HasLeftThroughSale(item.Status))
        {
            return current is null ? null : StoreListingStatus.Sold;
        }

        if (item.Status == InventoryStatus.Listed)
        {
            // Listed items ALWAYS have a listing row: Active when shoppable, Draft ("awaiting photos" and friends)
            // when not — the operator's to-do queue, invisible to shoppers either way until Active.
            return IsShoppable(item) ? StoreListingStatus.Active : StoreListingStatus.Draft;
        }

        // Any other stage (ReadyToList, NeedsPhotos, Returned, Donated…): off the shelf, kept as Draft for relisting.
        return current is null ? null : StoreListingStatus.Draft;
    }
}
