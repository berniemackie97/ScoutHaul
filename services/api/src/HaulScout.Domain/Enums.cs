namespace HaulScout.Domain;

public enum OrganizationRole
{
    Owner,
    Member
}

public enum InventoryStatus
{
    Draft,
    Researching,
    Watchlist,
    Purchased,
    NeedsCleaning,
    NeedsTesting,
    NeedsRepair,
    NeedsPhotos,
    ReadyToList,
    Bundled,
    Listed,
    Sold,
    Packed,
    Shipped,
    Delivered,
    Returned,
    Donated,
    Disposed
}

/// <summary>
/// "Needs operator attention" buckets the inventory browse can filter to — the sets the dashboard/analytics action
/// cards count. These are not lifecycle statuses; each maps to a computed predicate in the inventory query.
/// </summary>
public enum InventoryAttention
{
    Unlisted,
    Aged,
    MissingPhotos,
    MissingCost,
    MissingValue,
    MissingLocation
}

public enum InventoryOwnershipType
{
    Owned,
    Consigned,
    CustomerRepair,
    VendorManaged
}

public enum InventoryFunctionalStatus
{
    Unknown,
    Untested,
    FullyFunctional,
    FunctionalWithIssues,
    Intermittent,
    NonFunctional,
    PartsOnly
}

public enum InventoryCompletenessStatus
{
    Unknown,
    Complete,
    MissingAccessories,
    MissingParts,
    Incomplete
}

public enum InventoryAuthenticityStatus
{
    NotAssessed,
    Authentic,
    LikelyAuthentic,
    NeedsVerification,
    Replica,
    Counterfeit
}

public enum InventoryPackagingStatus
{
    NotApplicable,
    FactorySealed,
    OriginalExcellent,
    OriginalWorn,
    OriginalDamaged,
    ReplacementPackaging,
    LooseNoPackaging
}

public enum DimensionUnit
{
    Inches,
    Centimeters
}

public enum WeightUnit
{
    Ounces,
    Pounds,
    Grams,
    Kilograms
}

public enum MarketObservationType
{
    Sold,
    ActiveListing,
    CompletedUnsold,
    ManualSold,
    ManualActive,
    InternalSale,
    Estimate,
    Unknown
}

public enum PricingBasisType
{
    LiveValidated,
    CachedRecent,
    CachedStale,
    ManualEstimate,
    InternalHistory,
    Unavailable
}

public enum RecommendationDecisionType
{
    Buy,
    Negotiate,
    Pass,
    ResearchRequired,
    Unavailable
}

public enum ListingStatus
{
    Draft,
    Active,
    Ended,
    Sold,
    Cancelled
}

public enum SaleStatus
{
    Recorded,
    Packed,
    Shipped,
    Delivered,
    ReturnRequested,
    Returned,
    Refunded,
    Cancelled,
    /// <summary>The buyer was refunded part of the order but kept the item — the sale stays realized with
    /// reduced revenue. Appended (not reordered) so existing stored status integers are unchanged.</summary>
    PartiallyRefunded
}

/// <summary>How much of an order a refund covers. Drives the sale's resulting status and inventory release.</summary>
public enum RefundType
{
    /// <summary>Buyer kept the item, refunded part of what they paid. Sale stays realized (reduced).</summary>
    Partial,
    /// <summary>Full refund — the whole item value is returned. Optionally the item comes back to inventory.</summary>
    Full
}

/// <summary>Stages of a return (RMA). A refund is issued only once the item is back and inspected (Received), never
/// at Requested — the seller pays the buyer's return shipping up front and takes the money hit whatever the outcome.</summary>
public enum ReturnStatus
{
    /// <summary>Buyer asked to return; nothing paid or moved yet.</summary>
    Requested,
    /// <summary>Return shipping label issued to the buyer (the seller's cost is now booked).</summary>
    LabelIssued,
    /// <summary>Item received back and inspected — ready to refund.</summary>
    Received,
    /// <summary>Item received and the refund issued (and restocked if resellable). Terminal.</summary>
    Refunded,
    /// <summary>Return denied after inspection (e.g. not the item, damaged) — no or reduced refund. Terminal.</summary>
    Rejected,
    /// <summary>Return abandoned before completion. Terminal.</summary>
    Cancelled
}

public enum WorkTaskStatus
{
    Open,
    InProgress,
    Blocked,
    Completed,
    Cancelled
}

/// <summary>State of a marketplace order imported from a platform report or supplied as normalized operator data.</summary>
public enum PlatformOrderStatus
{
    /// <summary>Imported from the platform, not yet turned into sales.</summary>
    Imported,
    /// <summary>Reconciled into ScoutHaul sales (linked via PlatformOrder.ReconciledSaleGroupId).</summary>
    Reconciled,
    /// <summary>Deliberately excluded (e.g. a test order or a non-inventory sale).</summary>
    Ignored
}

/// <summary>The marketplace's own state for an imported or helper-captured listing, not just live ones.</summary>
public enum PlatformListingStatus
{
    Active,
    Draft,
    Scheduled,
    Ended,
    Sold,
    Unsold,
    OutOfStock
}

/// <summary>Whether an imported or helper-captured listing has been tied into ScoutHaul inventory yet.</summary>
public enum PlatformListingReconcileStatus
{
    /// <summary>Imported or captured, not yet linked to an inventory item.</summary>
    Pending,
    /// <summary>Linked to an inventory item + ChannelListing.</summary>
    Linked,
    /// <summary>Deliberately excluded from import.</summary>
    Ignored
}

public enum SourcingRouteStatus
{
    Draft,
    Planned,
    Active,
    Paused,
    Completed,
    Cancelled
}

public enum SourcingRouteType
{
    General,
    GarageSales,
    ThriftRun,
    PickupRun,
    AuctionPickup,
    Liquidation,
    Mixed
}

public enum SourcingStopSourceType
{
    GarageSale,
    EstateSale,
    ThriftStore,
    Goodwill,
    FacebookPickup,
    Auction,
    Liquidation,
    FleaMarket,
    Other
}

public enum SourcingStopPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum SourcingStopStatus
{
    Planned,
    Navigating,
    Arrived,
    InProgress,
    Completed,
    Skipped,
    Cancelled,
    AtRisk
}

public enum ExpenseCategory
{
    InventoryPurchase,
    Shipping,
    Supplies,
    PlatformFee,
    PaymentFee,
    Refund,
    Fuel,
    Storage,
    Other
}

public enum AcquisitionLotAllocationMethod
{
    Equal,
    WeightedByExpectedValue,
    Manual
}

// ── Storefront commerce ──────────────────────────────────────────
public enum StoreListingStatus
{
    Draft,
    Active,
    Reserved,
    Sold,
    Archived
}

public enum StoreShippingPolicy
{
    StoreDefault,
    Free,
    FixedAmount
}

public enum CartStatus
{
    Active,
    Converted,
    Abandoned
}

public enum OrderStatus
{
    Pending,
    Paid,
    Fulfilling,
    Shipped,
    Delivered,
    Cancelled,
    Refunded
}

public enum PaymentStatus
{
    Pending,
    Paid,
    PartiallyRefunded,
    Refunded,
    Failed
}

public enum FulfillmentStatus
{
    Unfulfilled,
    Partial,
    Fulfilled
}

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public enum StorefrontOfferStatus
{
    New,
    Accepted,
    Countered,
    Declined,
    Expired,
    Converted
}

/// <summary>State of an inventory item on a single channel account (the manual channel-presence ledger).</summary>
public enum ChannelListingStatus
{
    NotListed,
    Draft,
    Active,
    Sold,
    /// <summary>Sold on another channel — needs manual review/removal on this channel.</summary>
    PendingDelist,
    Delisted,
    Error
}
