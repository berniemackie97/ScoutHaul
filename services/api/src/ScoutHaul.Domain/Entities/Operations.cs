using ScoutHaul.Domain.Common;

namespace ScoutHaul.Domain.Entities;

public sealed class SourcingTrip : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public string? Notes { get; set; }
    public decimal? CashBudget { get; set; }
    public ICollection<InventoryItem> Items { get; set; } = [];
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
