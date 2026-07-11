using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A return (RMA) against a <see cref="Sale"/> line — the process, tracked as it moves through its stages, as
/// opposed to the <see cref="Refund"/>, which is the single money event issued at the end. The normal path is:
/// buyer requests → the seller issues a return shipping label (and eats that cost up front) → the item comes back
/// and is inspected → a refund is issued and the item is restocked if it is resellable. A refund is never issued at
/// request time. Rare "keep the item, refund anyway" cases skip this entirely and record a <see cref="Refund"/> directly.
/// </summary>
public sealed class Return : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }

    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }

    /// <summary>Denormalized from the sale's order so returns can be listed/rolled up per order without a join.</summary>
    public Guid? OrderId { get; set; }

    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;

    /// <summary>What the seller paid for the buyer's return shipping label. Booked as a cost when the label is
    /// issued, so it reduces net whether or not the return ends in a refund.</summary>
    public decimal ReturnShippingCost { get; set; }

    public required string Reason { get; set; }
    public string? Notes { get; set; }

    /// <summary>The refund issued when the item was received (null until then, and for rejected/cancelled returns).</summary>
    public Guid? RefundId { get; set; }
    public Refund? Refund { get; set; }

    /// <summary>True when the received item went back to sellable inventory (its COGS is freed from the sale).</summary>
    public bool Restocked { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LabelIssuedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }

    public bool IsOpen => Status is ReturnStatus.Requested or ReturnStatus.LabelIssued or ReturnStatus.Received;
}
