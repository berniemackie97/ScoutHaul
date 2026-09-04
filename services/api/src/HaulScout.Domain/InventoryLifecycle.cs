namespace HaulScout.Domain;

/// <summary>
/// Coarse lifecycle grouping over <see cref="InventoryStatus"/>. Lets filters and reports reason
/// about where an item is without re-deriving the status order on every surface.
/// </summary>
public enum InventoryLifecycleGroup
{
    /// <summary>Unsold and workable — intake through Listed.</summary>
    Active,

    /// <summary>Sold and moving through fulfillment — Sold, Packed, Shipped, Delivered.</summary>
    Sold,

    /// <summary>Came back from a buyer; available to re-list.</summary>
    Returned,

    /// <summary>Left inventory without a sale — Donated, Disposed.</summary>
    Removed,
}

/// <summary>
/// The single source of truth for inventory status ordering. A status later in
/// <see cref="Order"/> implies every earlier milestone (Shipped ⇒ already Sold), so callers can ask
/// "is this sold?" or "is this active?" instead of hard-coding status lists that drift out of sync.
/// </summary>
public static class InventoryLifecycle
{
    /// <summary>Canonical progression, earliest to latest. Index = rank.</summary>
    public static readonly IReadOnlyList<InventoryStatus> Order =
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
        InventoryStatus.Bundled,
        InventoryStatus.Listed,
        InventoryStatus.Sold,
        InventoryStatus.Packed,
        InventoryStatus.Shipped,
        InventoryStatus.Delivered,
        InventoryStatus.Returned,
        InventoryStatus.Donated,
        InventoryStatus.Disposed,
    ];

    /// <summary>Position in the lifecycle; higher = later. Unknown statuses sort last.</summary>
    public static int Rank(InventoryStatus status)
    {
        int index = Order.ToList().IndexOf(status);
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>True once an item has sold — Sold and every fulfillment stage past it.</summary>
    public static bool IsSold(InventoryStatus status) =>
        status is InventoryStatus.Sold
            or InventoryStatus.Packed
            or InventoryStatus.Shipped
            or InventoryStatus.Delivered;

    /// <summary>
    /// Whether an item going live somewhere — a channel, the storefront, a manual listing — should move
    /// it to <see cref="InventoryStatus.Listed"/>.
    ///
    /// False once it has reached Listed or anything after, so publishing a second channel cannot walk a
    /// sold item back to Listed and erase that it went. Bundled is excluded deliberately: a component of
    /// a bundle is not listed on its own, and the bundle is what goes on sale.
    /// </summary>
    public static bool ShouldAdvanceToListed(InventoryStatus status) =>
        status != InventoryStatus.Bundled && Rank(status) < Rank(InventoryStatus.Listed);

    public static InventoryLifecycleGroup Group(InventoryStatus status) => status switch
    {
        InventoryStatus.Sold or InventoryStatus.Packed or InventoryStatus.Shipped or InventoryStatus.Delivered
            => InventoryLifecycleGroup.Sold,
        InventoryStatus.Returned => InventoryLifecycleGroup.Returned,
        InventoryStatus.Donated or InventoryStatus.Disposed => InventoryLifecycleGroup.Removed,
        _ => InventoryLifecycleGroup.Active,
    };

    /// <summary>The statuses that belong to a group, for order-aware filtering.</summary>
    public static IReadOnlyList<InventoryStatus> InGroup(InventoryLifecycleGroup group) =>
        Order.Where(status => Group(status) == group).ToArray();
}
