using HaulScout.Domain.Entities;

namespace HaulScout.Domain;

/// <summary>
/// A bundle's components follow the bundle. When a bundle is sold, shipped or returned, the items inside it reached
/// that state too — they are physically in the box. This lives in the domain because it is a rule about what is true
/// of an item, and because both inventory and sales need it: the sales workflows are its main callers.
/// </summary>
public static class InventoryStatePropagation
{
    public static void ApplyBundleComponentInventoryStatus(
        InventoryItem inventoryItem,
        InventoryStatus componentStatus,
        DateTimeOffset? soldAt)
    {
        if (!inventoryItem.IsBundle)
        {
            return;
        }

        foreach (InventoryBundleComponent componentLink in inventoryItem.BundleComponents)
        {
            if (componentLink.ComponentInventoryItem is null)
            {
                continue;
            }

            componentLink.ComponentInventoryItem.Status = componentStatus;
            componentLink.ComponentInventoryItem.SoldAt = InventoryLifecycle.IsSold(componentStatus) || componentStatus == InventoryStatus.Returned
                ? soldAt
                : null;
        }
    }
}
