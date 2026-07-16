using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A return (RMA) against a <see cref="Sale"/> line — the process, tracked as it moves through its stages, as
/// opposed to the <see cref="Refund"/>, which is the single money event issued at the end. The normal path is:
/// buyer requests → the seller issues a return shipping label (and eats that cost up front) → the item comes back
/// and is inspected → a refund is issued and the item is restocked if it is resellable. A refund is never issued at
/// request time. Rare "keep the item, refund anyway" cases skip this entirely and record a <see cref="Refund"/> directly.
///
/// The stages are enforced here rather than by whoever calls: state changes only through the methods below, so a
/// return cannot be refunded before the item is back, relabelled after it has shipped, or resolved twice — no matter
/// how many callers there eventually are. Every method takes the moment it happened rather than reading a clock, so
/// the caller owns time and these rules can be tested without one.
/// </summary>
public sealed class Return : EntityBase, IOrganizationScoped
{
    private Return() { }   // EF materializes through this; everyone else goes through Open().

    public Guid OrganizationId { get; set; }

    public Guid SaleId { get; private set; }
    public Sale? Sale { get; set; }

    /// <summary>Denormalized from the sale's order so returns can be listed/rolled up per order without a join.</summary>
    public Guid? OrderId { get; private set; }

    public ReturnStatus Status { get; private set; } = ReturnStatus.Requested;

    /// <summary>What the seller paid for the buyer's return shipping label. Booked as a cost when the label is
    /// issued, so it reduces net whether or not the return ends in a refund.</summary>
    public decimal ReturnShippingCost { get; private set; }

    public string Reason { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    /// <summary>The refund issued when the item was received (null until then, and for rejected/cancelled returns).</summary>
    public Guid? RefundId { get; private set; }
    public Refund? Refund { get; private set; }

    /// <summary>True when the received item went back to sellable inventory (its COGS is freed from the sale).</summary>
    public bool Restocked { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? LabelIssuedAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid RequestedByUserId { get; private set; }
    public AppUser? RequestedByUser { get; set; }

    public bool IsOpen => Status is ReturnStatus.Requested or ReturnStatus.LabelIssued or ReturnStatus.Received;

    /// <summary>Opens a return against a sale line. A reason is always required — it is the record of why.</summary>
    public static Return Open(
        Guid organizationId,
        Sale sale,
        string reason,
        string? notes,
        Guid requestedByUserId,
        DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required to open a return.");
        }

        return new Return
        {
            OrganizationId = organizationId,
            SaleId = sale.Id,
            Sale = sale,
            OrderId = sale.OrderId,
            Status = ReturnStatus.Requested,
            Reason = reason.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            RequestedByUserId = requestedByUserId,
            RequestedAt = at,
        };
    }

    /// <summary>The seller sends the buyer a label and eats the cost now — before knowing how this ends.</summary>
    public void IssueLabel(decimal returnShippingCost, string? notes, DateTimeOffset at)
    {
        if (returnShippingCost < 0)
        {
            throw new DomainException("Return shipping cost cannot be negative.");
        }
        if (Status != ReturnStatus.Requested)
        {
            throw new DomainException($"A return label can only be issued from Requested (it is {Status}).");
        }

        ReturnShippingCost = returnShippingCost;
        Status = ReturnStatus.LabelIssued;
        LabelIssuedAt = at;
        if (notes is not null)
        {
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        }
    }

    /// <summary>The item is physically back and inspected. Only now can it be refunded.</summary>
    public void Receive(DateTimeOffset at)
    {
        if (Status is not (ReturnStatus.Requested or ReturnStatus.LabelIssued))
        {
            throw new DomainException($"Only a Requested or LabelIssued return can be marked received (it is {Status}).");
        }

        Status = ReturnStatus.Received;
        ReceivedAt = at;
    }

    /// <summary>Closes the return with the money event. Issued once, and never before the item is back.</summary>
    public void Resolve(Refund refund, bool restocked, DateTimeOffset at)
    {
        if (Status != ReturnStatus.Received)
        {
            throw new DomainException($"A refund is issued once the return is Received (it is {Status}). Mark it received first.");
        }

        Status = ReturnStatus.Refunded;
        Refund = refund;
        RefundId = refund.Id;
        Restocked = restocked;
        ResolvedAt = at;
    }

    /// <summary>Refused after inspection. Any label already paid for stays booked — the seller ate that regardless.</summary>
    public void Reject(string reason, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required to reject a return.");
        }
        if (!IsOpen)
        {
            throw new DomainException($"This return is already resolved ({Status}).");
        }

        Status = ReturnStatus.Rejected;
        ResolvedAt = at;
        Notes = reason.Trim();
    }

    /// <summary>Called off before the item came back.</summary>
    public void Cancel(DateTimeOffset at)
    {
        if (Status is not (ReturnStatus.Requested or ReturnStatus.LabelIssued))
        {
            throw new DomainException($"Only a Requested or LabelIssued return can be cancelled (it is {Status}).");
        }

        Status = ReturnStatus.Cancelled;
        ResolvedAt = at;
    }
}
