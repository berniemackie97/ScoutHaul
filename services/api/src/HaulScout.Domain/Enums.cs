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

public enum WorkTaskStatus
{
    Open,
    InProgress,
    Blocked,
    Completed,
    Cancelled
}

/// <summary>State of a scraped marketplace order captured by the extension.</summary>
public enum PlatformOrderStatus
{
    /// <summary>Imported from the platform, not yet turned into sales.</summary>
    Imported,
    /// <summary>Reconciled into ScoutHaul sales (linked via PlatformOrder.ReconciledSaleGroupId).</summary>
    Reconciled,
    /// <summary>Deliberately excluded (e.g. a test order or a non-inventory sale).</summary>
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

/// <summary>State of an inventory item on a single channel account (the cross-listing sync ledger).</summary>
public enum ChannelListingStatus
{
    NotListed,
    Draft,
    Active,
    Sold,
    /// <summary>Sold on another channel — needs to be taken down here. The extension surfaces these to delist.</summary>
    PendingDelist,
    Delisted,
    Error
}
