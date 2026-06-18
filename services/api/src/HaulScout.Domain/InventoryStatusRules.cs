namespace HaulScout.Domain;

public static class InventoryStatusRules
{
    private static readonly HashSet<InventoryStatus> CurrentInventoryStatuses =
    [
        InventoryStatus.Draft,
        InventoryStatus.Researching,
        InventoryStatus.Watchlist,
        InventoryStatus.Purchased,
        InventoryStatus.NeedsCleaning,
        InventoryStatus.NeedsTesting,
        InventoryStatus.NeedsRepair,
        InventoryStatus.NeedsPhotos,
        InventoryStatus.ReadyToList,
        InventoryStatus.Listed,
        InventoryStatus.Packed
    ];

    private static readonly HashSet<InventoryStatus> UnlistedStatuses =
    [
        InventoryStatus.Purchased,
        InventoryStatus.NeedsCleaning,
        InventoryStatus.NeedsTesting,
        InventoryStatus.NeedsRepair,
        InventoryStatus.NeedsPhotos,
        InventoryStatus.ReadyToList
    ];

    public static bool IsCurrentInventory(InventoryStatus status) => CurrentInventoryStatuses.Contains(status);

    public static bool IsUnlistedInventory(InventoryStatus status) => UnlistedStatuses.Contains(status);
}
