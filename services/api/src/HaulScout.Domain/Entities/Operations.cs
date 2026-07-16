using HaulScout.Domain.Common;
using HaulScout.Domain.Services;

namespace HaulScout.Domain.Entities;

public sealed class SourcingTrip : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public AppUser? AssignedToUser { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public DateOnly? RouteDate { get; set; }
    public DateTimeOffset? PlannedStartAt { get; set; }
    public DateTimeOffset? PlannedEndAt { get; set; }
    public SourcingRouteType RouteType { get; set; } = SourcingRouteType.General;
    public SourcingRouteStatus Status { get; set; } = SourcingRouteStatus.Draft;
    public string? Notes { get; set; }
    public decimal? CashBudget { get; set; }
    public ICollection<InventoryItem> Items { get; set; } = [];
    public ICollection<SourcingTripStop> Stops { get; set; } = [];
}

public sealed class SourcingTripStop : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid SourcingTripId { get; set; }
    public SourcingTrip? SourcingTrip { get; set; }
    public required string Name { get; set; }
    public SourcingStopSourceType SourceType { get; set; } = SourcingStopSourceType.Other;
    public required string Address { get; set; }
    public string? Phone { get; set; }
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public int PlannedOrder { get; set; }
    public DateTimeOffset? PlannedArrivalAt { get; set; }
    public int EstimatedDurationMinutes { get; set; } = 30;
    public SourcingStopPriority Priority { get; set; } = SourcingStopPriority.Medium;
    public bool IsTimeSensitive { get; set; }
    public DateTimeOffset? TimeWindowStartAt { get; set; }
    public DateTimeOffset? TimeWindowEndAt { get; set; }
    public bool CanBeSkipped { get; set; } = true;
    public decimal? BudgetCap { get; set; }
    public SourcingStopStatus Status { get; set; } = SourcingStopStatus.Planned;
    public DateTimeOffset? ActualArrivalAt { get; set; }
    public DateTimeOffset? ActualDepartureAt { get; set; }
    public ICollection<SourcingStopChecklistItem> ChecklistItems { get; set; } = [];
    public ICollection<InventoryItem> Items { get; set; } = [];
}

public sealed class SourcingStopChecklistItem : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid SourcingTripStopId { get; set; }
    public SourcingTripStop? SourcingTripStop { get; set; }
    public required string Text { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class Listing : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public required string Platform { get; set; }
    public string? ListingUrl { get; set; }
    public decimal ListPrice { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public DateTimeOffset? ListedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class Sale : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public Guid? ListingId { get; set; }
    public Listing? Listing { get; set; }
    public required string Platform { get; set; }
    public decimal SoldPrice { get; set; }
    /// <summary>Shipping/handling charged to the buyer. This is revenue, distinct from seller shipping label cost.</summary>
    public decimal ShippingCharged { get; set; }
    public decimal CostOfGoods { get; set; }
    public decimal PlatformFees { get; set; }
    public decimal PaymentFees { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal SuppliesCost { get; set; }
    public decimal OtherCosts { get; set; }

    /// <summary>Sales tax charged on the order. Never part of net profit (see <see cref="TaxRemittedByPlatform"/>).</summary>
    public decimal SalesTaxCollected { get; set; }

    /// <summary>
    /// True when the marketplace collected and remits the sales tax (facilitator) — pass-through, neither
    /// revenue nor liability. False when we collected it and owe it to the state — a sales-tax liability.
    /// </summary>
    public bool TaxRemittedByPlatform { get; set; } = true;

    /// <summary>
    /// Derived, never assigned: the only writer is <see cref="RecomputeNet"/>. Private-set is the guardrail — the
    /// refund-blind duplicates that used to set this directly now cannot compile.
    /// </summary>
    public decimal NetProfit { get; private set; }
    public SaleStatus Status { get; set; } = SaleStatus.Recorded;
    public DateTimeOffset SoldAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }

    /// <summary>The order this sale is a line of — the same <see cref="Entities.Order"/> used by the storefront,
    /// so an order is an order on every channel. This supersedes <see cref="SaleGroupId"/>.</summary>
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>LEGACY — superseded by <see cref="OrderId"/>. Kept only so existing rows still read back; new
    /// writes must set <see cref="OrderId"/>. Dropped in the contract migration once backfill completes.</summary>
    public Guid? SaleGroupId { get; set; }

    /// <summary>LEGACY — superseded by <c>Order.ExternalOrderId</c>. Kept only for read-back of existing rows.</summary>
    public string? GroupOrderReference { get; set; }

    public ICollection<SaleCorrection> Corrections { get; set; } = [];
    public ICollection<Refund> Refunds { get; set; } = [];
    public ICollection<Return> Returns { get; set; } = [];

    /// <summary>
    /// The aggregate refund impact on this sale, derived from its own refunds rather than passed in — so no caller
    /// can compute it from a partial view.
    /// </summary>
    private ResaleFinancialCalculator.SaleRefundImpact RefundImpact() => new(
        Refunds.Sum(refund => refund.ItemAmount),
        Refunds.Sum(refund => refund.ShippingRefunded),
        Refunds.Sum(refund => refund.TaxRefunded),
        Refunds.Sum(refund => refund.FeeCredit),
        Refunds.Any(refund => refund.ItemReturned));

    /// <summary>
    /// Re-derives <see cref="NetProfit"/> from this sale's current figures <em>and</em> its refunds. This is the only
    /// way net is ever set, which is the entire point: it used to be derived at seven call sites, five of which used
    /// the refund-blind calculator, so correcting a refunded sale silently handed the refunded money back as profit.
    /// Refunds are part of the derivation here, so a caller cannot forget them.
    ///
    /// Requires <see cref="Refunds"/> to be loaded — an unloaded collection would read as "no refunds" and produce
    /// exactly the wrong number this method exists to prevent. Callers must Include it (enforced by the tests).
    /// </summary>
    public void RecomputeNet() => NetProfit = ResaleFinancialCalculator.NetProfitAfterRefunds(
        SoldPrice, ShippingCharged, CostOfGoods, PlatformFees, PaymentFees,
        ShippingCost, SuppliesCost, OtherCosts, RefundImpact());

    /// <summary>
    /// Re-derives net <em>and</em> the resulting sale status from the current refund set. Full/partial and whether the
    /// item came back decide the status; an emptied refund set reverts to a realized sale. The matching inventory
    /// transition is a different aggregate's business and stays with the caller.
    /// </summary>
    /// <returns>True when the refunds mean the item is back in our hands and inventory should follow.</returns>
    public bool ApplyRefunds()
    {
        RecomputeNet();

        if (Refunds.Count == 0)
        {
            // Last refund voided — back to a realized state; the operator can re-set the exact status.
            Status = SaleStatus.Delivered;
            return false;
        }

        decimal itemRefunded = Refunds.Sum(refund => refund.ItemAmount);
        bool restocked = Refunds.Any(refund => refund.ItemReturned);
        bool isFull = Refunds.Any(refund => refund.Type == RefundType.Full)
                      || (SoldPrice > 0 && itemRefunded >= SoldPrice - 0.01m);

        Status = restocked ? SaleStatus.Returned : isFull ? SaleStatus.Refunded : SaleStatus.PartiallyRefunded;
        return restocked;
    }
}

/// <summary>
/// An immutable audit record of one edit to a <see cref="Sale"/>. Corrections never erase prior state —
/// each captures who, when, why, and the full before/after financial snapshot. Posted history is preserved.
/// </summary>
public sealed class SaleCorrection : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }
    public Guid CorrectedByUserId { get; set; }
    public AppUser? CorrectedByUser { get; set; }
    public required string Reason { get; set; }
    public required string BeforeJson { get; set; }
    public required string AfterJson { get; set; }
    /// <summary>1-based sequence: the Nth correction recorded for a sale has Version = N.</summary>
    public int Version { get; set; }
}

public sealed class Shipment : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public decimal? Cost { get; set; }
    public DateTimeOffset? PackedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class Expense : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public Guid? AcquisitionLotId { get; set; }
    public AcquisitionLot? AcquisitionLot { get; set; }
    public Guid? SourcingTripId { get; set; }
    public Guid? SourcingTripStopId { get; set; }
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Budget : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public decimal StartingAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ResaleTask : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public AppUser? AssignedToUser { get; set; }
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Open;
    public DateTimeOffset? DueAt { get; set; }
}

public sealed class AuditLog : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string EntityName { get; set; }
    public Guid? EntityId { get; set; }
    public required string Action { get; set; }
    public string? DetailsJson { get; set; }
}
