using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A marketplace listing captured from the page the operator is viewing or imported from a platform export — in
/// any state (active, draft, scheduled, ended, sold, unsold, out of stock), not just live ones. This is the
/// idempotent landing zone for intentional listing imports: re-ingesting the same listing id updates it in place.
/// Reconciling a listing seeds/links a ScoutHaul <see cref="InventoryItem"/> and its <see cref="ChannelListing"/>
/// so existing listings populate inventory without re-entry. No marketplace API or unattended scraping is used.
/// </summary>
public sealed class PlatformListing : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid? ChannelAccountId { get; set; }
    public ChannelAccount? ChannelAccount { get; set; }

    /// <summary>SalesChannelCatalog value, e.g. "eBay".</summary>
    public required string Platform { get; set; }

    /// <summary>The platform's own listing id (eBay item id) — unique per org+platform; the ingest idempotency key.</summary>
    public required string ExternalListingId { get; set; }

    public required string Title { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
    public int? QuantitySold { get; set; }
    public string Currency { get; set; } = "USD";

    public PlatformListingStatus Status { get; set; } = PlatformListingStatus.Active;

    /// <summary>"FixedPrice" or "Auction" as the platform reports it.</summary>
    public string? Format { get; set; }
    public string? Sku { get; set; }
    public string? CategoryName { get; set; }
    public string? Condition { get; set; }
    public string? Url { get; set; }
    public string? PrimaryImageUrl { get; set; }

    public DateTimeOffset? ListedAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }

    public PlatformListingReconcileStatus ReconcileStatus { get; set; } = PlatformListingReconcileStatus.Pending;
    public Guid? ReconciledInventoryItemId { get; set; }
    public InventoryItem? ReconciledInventoryItem { get; set; }
    public Guid? ReconciledChannelListingId { get; set; }
    /// <summary>
    /// True when reconciling this imported listing created the linked <see cref="ChannelListing"/>.
    /// Used by unlink so we can remove records ScoutHaul created without deleting a legitimate
    /// pre-existing channel listing that was merely matched.
    /// </summary>
    public bool CreatedChannelListingOnReconcile { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }

    public DateTimeOffset ScrapedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Full imported/captured payload for audit and re-processing if mapping logic improves later.</summary>
    public string? RawJson { get; set; }
}
