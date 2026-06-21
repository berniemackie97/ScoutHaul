using HaulScout.Domain.Common;

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
    public decimal CostOfGoods { get; set; }
    public decimal PlatformFees { get; set; }
    public decimal PaymentFees { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal SuppliesCost { get; set; }
    public decimal OtherCosts { get; set; }
    public decimal NetProfit { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Recorded;
    public DateTimeOffset SoldAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
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
