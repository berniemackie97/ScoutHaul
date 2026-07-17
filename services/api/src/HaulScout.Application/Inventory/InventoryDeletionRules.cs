using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// When an item may be archived, and when it may actually be destroyed. These are pure functions of the item and what
/// still references it, so the reasons an operator gets told can be tested directly.
///
/// The distinction that matters: archiving hides an item, deleting erases it. Anything that is financial or customer
/// history therefore blocks a hard delete but not an archive — deleting it would silently rewrite the books.
/// </summary>
public static class InventoryDeletionRules
{
    /// <summary>Why this item cannot be archived or deleted at all, or null if it can. Live commitments block both:
    /// letting an item vanish while it is still listed or sold would leave the other side pointing at nothing.</summary>
    public static string? BlockedFromRemoval(InventoryItem item, InventoryReferences references)
    {
        if (item.Status == InventoryStatus.Listed)
        {
            return "This inventory item is marked Listed. Delist it from every channel and move it out of the Listed stage before deleting or archiving it.";
        }

        if (references.HasActiveSale)
        {
            return "This inventory item has an active sale. Complete, cancel, refund, or return the sale workflow before deleting or archiving it.";
        }

        if (references.IsBundleComponent)
        {
            return "This item is inside a bundle. Delete or edit the bundle before deleting or archiving the component item.";
        }

        if (references.HasLiveChannelListing)
        {
            return "This inventory item still has live or pending-delist channel listings. Delist every channel before deleting or archiving it.";
        }

        if (references.HasActiveMarketplaceListing)
        {
            return "This inventory item still has an active marketplace listing. End or delist the listing before deleting or archiving it.";
        }

        if (references.HasActiveStorefrontListing)
        {
            return "This inventory item is still active or reserved on the storefront. Archive the storefront listing before deleting or archiving the item.";
        }

        return null;
    }

    /// <summary>Why this item may only be archived rather than destroyed, or null if erasing it is safe. History is
    /// the whole test: once an item has been sold or ordered, deleting the row would rewrite the record of what
    /// happened, so the operator is pushed toward archiving instead.</summary>
    public static string? BlockedFromHardDelete(InventoryReferences references)
    {
        if (references.HasSaleHistory)
        {
            return "This inventory item has sale history. Archive it instead so financial reporting and corrections remain intact.";
        }

        if (references.HasOrderHistory)
        {
            return "This inventory item is attached to a customer order. Archive it instead so customer/order history remains intact.";
        }

        if (references.IsBundleComponent)
        {
            return "This item is inside a bundle. Delete or edit the bundle before deleting the component item.";
        }

        return null;
    }

    /// <summary>
    /// A bundle's components outlive it. When the bundle goes, anything still marked Bundled is returned to
    /// ReadyToList — otherwise the components would be stranded in a stage whose bundle no longer exists.
    /// </summary>
    public static void ReleaseBundleComponents(InventoryItem item)
    {
        if (!item.IsBundle)
        {
            return;
        }

        foreach (InventoryBundleComponent component in item.BundleComponents)
        {
            if (component.ComponentInventoryItem is { Status: InventoryStatus.Bundled } component_)
            {
                component_.Status = InventoryStatus.ReadyToList;
            }
        }
    }
}
