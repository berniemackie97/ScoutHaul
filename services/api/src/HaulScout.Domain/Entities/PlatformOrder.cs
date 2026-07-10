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

    // Exactly as the platform reports them.
    public decimal ItemsSubtotal { get; set; }
    public decimal ShippingCharged { get; set; }
    public decimal TaxCollected { get; set; }
    public decimal FeeTotal { get; set; }

    /// <summary>The seller's own shipping-label cost, when the platform reports it (e.g. eBay labels bought on
    /// eBay). Null when unknown — reconciliation then preserves whatever cost the operator already recorded.</summary>
    public decimal? SellerShippingCost { get; set; }

    public decimal? PayoutAmount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Whether the marketplace remitted the collected tax (pass-through) vs the seller owing it.</summary>
    public bool TaxRemittedByPlatform { get; set; } = true;

    /// <summary>Exact fee lines as reported/imported (final value, per-order, ad, international…), for audit + display.</summary>
    public string? FeeBreakdownJson { get; set; }

    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }

    public PlatformOrderStatus Status { get; set; } = PlatformOrderStatus.Imported;

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
}
