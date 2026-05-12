namespace ScoutHaul.Domain;

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
    Returned
}

public enum WorkTaskStatus
{
    Open,
    InProgress,
    Blocked,
    Completed,
    Cancelled
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
