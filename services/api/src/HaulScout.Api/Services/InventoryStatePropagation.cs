using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Api.Services;

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
