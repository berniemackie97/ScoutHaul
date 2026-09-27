using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A marketplace order imported from platform reports or manually supplied normalized data. This is the
/// idempotent landing zone for the platform's OWN numbers — exact item prices,
/// shipping, tax, fees, and payout as the marketplace reports them — so ScoutHaul's finances match the
/// platform by definition. Reconciling an order creates the ScoutHaul <see cref="Sale"/>(s) it represents.
/// No marketplace API or unattended marketplace scraping is involved.
/// </summary>
public sealed class PlatformOrder : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid? ChannelAccountId { get; set; }
    public ChannelAccount? ChannelAccount { get; set; }

    /// <summary>SalesChannelCatalog value, e.g. "eBay".</summary>
    public required string Platform { get; set; }

    /// <summary>The platform's own order id — unique per org+platform; the idempotency key for ingest.</summary>
    public required string ExternalOrderId { get; set; }

    public string? BuyerName { get; set; }
    public string? BuyerReference { get; set; }
    public string? BuyerEmail { get; set; }
    public string? BuyerNote { get; set; }

    public string? ShipToName { get; set; }
    public string? ShipToPhone { get; set; }
    public string? ShipToLine1 { get; set; }
    public string? ShipToLine2 { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToRegion { get; set; }
    public string? ShipToPostalCode { get; set; }
    public string? ShipToCountry { get; set; }

    public DateTimeOffset OrderedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    public PlatformFulfillmentStage FulfillmentStage { get; set; } = PlatformFulfillmentStage.Unknown;

    /// <summary>The marketplace's own words for that stage ("Carrier scan Sep 24"), shown as it wrote them.</summary>
    public string? FulfillmentDetail { get; set; }

    // Exactly as the platform reports them.
    public decimal ItemsSubtotal { get; set; }
    public decimal ShippingCharged { get; set; }
    public decimal TaxCollected { get; set; }
    public decimal FeeTotal { get; set; }

    /// <summary>
    /// True while the marketplace has not yet posted this order's fees — a scan saw the sale minutes
    /// after it happened, before the fees reached the seller's ledger. <see cref="FeeTotal"/> means
    /// nothing until it clears. The order is still a sale for stock purposes, but it is not settled
    /// into money until a later scan or report brings the fees.
    /// </summary>
    public bool FeesPending { get; set; }

    /// <summary>The seller's own shipping-label cost, when the platform reports it (e.g. eBay labels bought on
    /// eBay). Null when unknown — reconciliation then preserves whatever cost the operator already recorded.</summary>
    public decimal? SellerShippingCost { get; set; }

    public decimal? PayoutAmount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>The keys were made up from the sale's details, by a source with no ids of its own; a real id replaces them.</summary>
    public bool KeysAreDerived { get; set; }

    /// <summary>Whether the marketplace remitted the collected tax (pass-through) vs the seller owing it.</summary>
    public bool TaxRemittedByPlatform { get; set; } = true;

    /// <summary>Exact fee lines as reported/imported (final value, per-order, ad, international…), for audit + display.</summary>
    public string? FeeBreakdownJson { get; set; }

    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }

    public PlatformOrderStatus Status { get; set; } = PlatformOrderStatus.Imported;

    /// <summary>
    /// When the marketplace said this order was cancelled. An order not yet reconciled is simply set aside; one
    /// already recorded as a sale cannot be, because undoing a sale moves stock and money — so it waits here for a
    /// person, rather than being reversed behind their back or lost in a scan's notes.
    /// </summary>
    public DateTimeOffset? CancelledOnPlatformAt { get; set; }

    public PlatformOrderCancellationOutcome CancellationOutcome { get; set; } = PlatformOrderCancellationOutcome.None;

    public DateTimeOffset? CancellationDecidedAt { get; set; }

    public string? CancellationNote { get; set; }

    public const int CancellationNoteLimit = 500;

    /// <summary>Column widths: the schema's, and what ingest fits text to.</summary>
    public static class Limits
    {
        public const int ExternalOrderId = 120;
        public const int BuyerName = 200;
        public const int BuyerReference = 200;
        public const int BuyerEmail = 320;
        public const int BuyerNote = 2000;
        public const int ShipToName = 160;
        public const int ShipToPhone = 40;
        public const int ShipToLine = 240;
        public const int ShipToCity = 120;
        public const int ShipToRegion = 120;
        public const int ShipToPostalCode = 32;
        public const int ShipToCountry = 80;
        public const int Carrier = 60;
        public const int TrackingNumber = 120;
        public const int FulfillmentDetail = 200;
    }

    /// <summary>A cancelled sale nobody has ruled on yet: the books still say it happened.</summary>
    public bool AwaitingCancellationDecision =>
        CancelledOnPlatformAt is not null && CancellationOutcome == PlatformOrderCancellationOutcome.None;

    /// <summary>Links to the sale group created when this order was reconciled.</summary>
    public Guid? ReconciledSaleGroupId { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }

    public DateTimeOffset ScrapedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Full imported payload for audit and re-processing if mapping logic improves later.</summary>
    public string? RawJson { get; set; }

    public ICollection<PlatformOrderLine> Lines { get; set; } = [];
}

public sealed class PlatformOrderLine : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid PlatformOrderId { get; set; }
    public PlatformOrder? PlatformOrder { get; set; }

    public required string Title { get; set; }

    /// <summary>The platform's listing/item id, used to auto-match to a ChannelListing.</summary>
    public string? ExternalItemId { get; set; }
    public string? Sku { get; set; }

    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal? ShippingCharged { get; set; }
    public decimal? TaxCollected { get; set; }
    public decimal? PlatformFees { get; set; }
    public decimal? SellerShippingCost { get; set; }
    public decimal? NetSales { get; set; }
    public decimal? TotalSales { get; set; }
    public string? FeeBreakdownJson { get; set; }
    public string? RawJson { get; set; }

    /// <summary>The ScoutHaul inventory item this line maps to (null until matched during reconcile).</summary>
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public static class Limits
    {
        public const int Title = 400;
        public const int ExternalItemId = 120;
        public const int Sku = 120;
    }
}
