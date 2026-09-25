using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A per-organization connection to a sales channel. The own storefront is a <see cref="IsNativelySynced"/>
/// channel kept in step by the API; external marketplaces (eBay, Poshmark, Depop, Vinted…) are manually tracked
/// from admin actions, report imports, and optional listing-helper captures. <see cref="Channel"/> is a
/// <c>SalesChannelCatalog</c> value so fee profiles and channel accounts share one taxonomy.
/// </summary>
public sealed class ChannelAccount : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>SalesChannelCatalog value, e.g. "eBay", "Poshmark", "Depop", "Vinted", "Storefront".</summary>
    public required string Channel { get; set; }

    /// <summary>Operator-facing name for this account, e.g. "Overhaul eBay Store".</summary>
    public required string DisplayName { get; set; }

    /// <summary>The account's username/handle/store id on the external platform.</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Default fee profile id from SalesChannelCatalog for this account (e.g. eBay "store-subscriber").</summary>
    public string? DefaultFeeProfileId { get; set; }

    /// <summary>True for the own storefront (kept in step natively by the API); false for manually tracked marketplaces.</summary>
    public bool IsNativelySynced { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ConnectedAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The marketplace account this channel account syncs, as a paired browser saw it signed in and
    /// the operator confirmed. A browser signed in as anyone else is refused, so a personal account
    /// open in the same browser can never be synced into the business by mistake.
    /// </summary>
    public string? BoundUsername { get; set; }

    /// <summary>The marketplace's own stable id for the bound user, where it exposes one.</summary>
    public string? BoundMarketplaceUserId { get; set; }

    public DateTimeOffset? BoundAt { get; set; }
    public Guid? BoundByUserId { get; set; }

    /// <summary>How often a paired browser scans this account's new sales. Null — the default — is off.</summary>
    public int? SalesScanEveryMinutes { get; set; }

    /// <summary>How often a paired browser snapshots this account's live listings. Null — the default — is off.</summary>
    public int? ListingsScanEveryMinutes { get; set; }

    /// <summary>
    /// Whether an item's photos are kept in step with this account's listings. Off until someone switches it on:
    /// the listing's photos then become the item's photos, replacing what the item had.
    /// </summary>
    public bool PhotoSyncEnabled { get; set; }

    public ICollection<ChannelListing> Listings { get; set; } = [];

    public bool IsBound => !string.IsNullOrWhiteSpace(BoundUsername);

    /// <summary>The schedule for one loop; null when it is off.</summary>
    public int? EveryMinutes(SyncLoopKind loop) => loop switch
    {
        SyncLoopKind.Sales => SalesScanEveryMinutes,
        SyncLoopKind.Listings => ListingsScanEveryMinutes,
        _ => null,
    };
}

/// <summary>
/// The state of one inventory item on one channel account — the channel-presence ledger. "Sold on eBay" flips
/// that row to Sold and flags other active marketplace rows for manual review; the API mirrors the storefront's
/// native state here so a single view shows every channel an item lives on.
/// </summary>
public sealed class ChannelListing : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public Guid ChannelAccountId { get; set; }
    public ChannelAccount? ChannelAccount { get; set; }

    public ChannelListingStatus Status { get; set; } = ChannelListingStatus.NotListed;

    /// <summary>The platform's own id for the listing (eBay item id, Poshmark listing id…).</summary>
    public string? ExternalListingId { get; set; }
    public string? ExternalUrl { get; set; }

    public decimal? ListedPrice { get; set; }
    public int? Quantity { get; set; }

    /// <summary>Optional hash of the images actually published on the channel, if a capture/import can provide it.</summary>
    public string? ImageHash { get; set; }

    public DateTimeOffset? ListedAt { get; set; }
    public DateTimeOffset? SoldAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Last channel tracking error surfaced to the operator (never silently swallowed).</summary>
    public string? LastError { get; set; }
}
