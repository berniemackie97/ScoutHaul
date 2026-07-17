namespace HaulScout.Application.Inventory;

/// <summary>
/// Everything still pointing at an inventory item, answered in one go. Deleting or archiving an item is gated on
/// several of these at once, and asking as a single question keeps the rules — which flag blocks what, and with which
/// explanation — as pure logic that can be tested without a database.
/// </summary>
/// <param name="HasActiveSale">A sale that has not released the item back (not returned/refunded/cancelled).</param>
/// <param name="HasSaleHistory">Any sale at all, live or long since settled. Financial history.</param>
/// <param name="IsBundleComponent">The item sits inside someone else's bundle.</param>
/// <param name="HasLiveChannelListing">Listed or pending-delist on a sales channel.</param>
/// <param name="HasActiveMarketplaceListing">An active marketplace listing.</param>
/// <param name="HasActiveStorefrontListing">Active or reserved on the storefront.</param>
/// <param name="HasOrderHistory">Attached to a customer order line.</param>
public sealed record InventoryReferences(
    bool HasActiveSale,
    bool HasSaleHistory,
    bool IsBundleComponent,
    bool HasLiveChannelListing,
    bool HasActiveMarketplaceListing,
    bool HasActiveStorefrontListing,
    bool HasOrderHistory);
