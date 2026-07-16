namespace HaulScout.Domain;

public static class SaleWorkflow
{
    public static readonly SaleStatus[] InventoryReleaseStatuses =
        [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

    private static readonly IReadOnlyDictionary<SaleStatus, SaleStatus[]> Allowed =
        new Dictionary<SaleStatus, SaleStatus[]>
        {
            [SaleStatus.Recorded] = [SaleStatus.Packed, SaleStatus.Cancelled],
            [SaleStatus.Packed] = [SaleStatus.Recorded, SaleStatus.Shipped, SaleStatus.Cancelled],
            [SaleStatus.Shipped] = [SaleStatus.Delivered, SaleStatus.ReturnRequested],
            [SaleStatus.Delivered] = [SaleStatus.ReturnRequested],
            [SaleStatus.ReturnRequested] = [SaleStatus.Delivered, SaleStatus.Returned],
            [SaleStatus.Returned] = [SaleStatus.Refunded],
            [SaleStatus.Refunded] = [],
            [SaleStatus.Cancelled] = [],
            // A partially refunded sale is still realized; it can later escalate to a return / full refund.
            [SaleStatus.PartiallyRefunded] = [SaleStatus.ReturnRequested, SaleStatus.Returned, SaleStatus.Refunded],
        };

    public static IReadOnlyList<SaleStatus> Next(SaleStatus status) =>
        Allowed.GetValueOrDefault(status) ?? [];

    public static bool CanTransition(SaleStatus from, SaleStatus to) => Next(from).Contains(to);

    public static bool RequiresReason(SaleStatus from, SaleStatus to) =>
        to is SaleStatus.Cancelled or SaleStatus.ReturnRequested or SaleStatus.Returned or SaleStatus.Refunded
        || (from == SaleStatus.Packed && to == SaleStatus.Recorded)
        || (from == SaleStatus.ReturnRequested && to == SaleStatus.Delivered);

    public static bool IsRealized(SaleStatus status) =>
        !InventoryReleaseStatuses.Contains(status);

    public static bool ReleasesInventory(SaleStatus status) =>
        InventoryReleaseStatuses.Contains(status);

    public static InventoryStatus InventoryStatusFor(SaleStatus status, bool hasListing) => status switch
    {
        SaleStatus.Recorded => InventoryStatus.Sold,
        SaleStatus.Packed => InventoryStatus.Packed,
        SaleStatus.Shipped => InventoryStatus.Shipped,
        SaleStatus.Delivered => InventoryStatus.Delivered,
        SaleStatus.ReturnRequested => InventoryStatus.Delivered,
        SaleStatus.Returned or SaleStatus.Refunded => InventoryStatus.Returned,
        SaleStatus.Cancelled => hasListing ? InventoryStatus.Listed : InventoryStatus.ReadyToList,
        // Partial refund: the buyer kept the item, so its inventory state is unchanged (stays sold/fulfilled).
        SaleStatus.PartiallyRefunded => InventoryStatus.Sold,
        _ => InventoryStatus.Sold,
    };
}
