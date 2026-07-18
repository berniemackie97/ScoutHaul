using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A refund issued against a <see cref="Sale"/> — full or partial — modeled as its own immutable financial event
/// rather than being smeared into a catch-all cost field. Each refund records exactly what the marketplace moved:
/// the item amount returned to the buyer, buyer shipping refunded, sales tax refunded (pass-through), and any
/// platform fee the marketplace credited the seller back. The sale's net profit is recomputed from the original
/// figures minus every refund, so the reported net always matches the marketplace to the cent.
/// </summary>
public sealed class Refund : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }

    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }

    public RefundType Type { get; set; } = RefundType.Partial;

    /// <summary>Item price refunded to the buyer (excludes shipping and tax).</summary>
    public decimal ItemAmount { get; set; }

    /// <summary>Buyer shipping/handling refunded.</summary>
    public decimal ShippingRefunded { get; set; }

    /// <summary>Sales tax refunded to the buyer — pass-through, never part of net; reduces tax collected.</summary>
    public decimal TaxRefunded { get; set; }

    /// <summary>Platform fee the marketplace credited the seller back on the refund (e.g. eBay FVF credit).
    /// Reduces the sale's effective selling cost, so it lifts net back up.</summary>
    public decimal FeeCredit { get; set; }

    /// <summary>True when the physical item came back and returns to sellable inventory (its COGS is no longer
    /// consumed by this sale). False for a partial "keep the item" refund.</summary>
    public bool ItemReturned { get; set; }

    public required string Reason { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The stable identity of this refund in its source, for refunds that come from an import rather than an
    /// operator's hand — e.g. "ebay:{order}:{item}". It makes re-importing the same marketplace report an
    /// idempotent UPSERT (find this key, refresh its amounts to the latest facts) instead of a duplicate or a
    /// skip. Null for refunds entered manually, which have no external source. Unique per organization.
    /// </summary>
    public string? ExternalRefundKey { get; set; }

    public DateTimeOffset RefundedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid RecordedByUserId { get; set; }
    public AppUser? RecordedByUser { get; set; }

    /// <summary>Item + shipping refunded to the buyer (tax excluded).</summary>
    public decimal BuyerRefundTotal => ItemAmount + ShippingRefunded;
}
