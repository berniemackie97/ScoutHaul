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
