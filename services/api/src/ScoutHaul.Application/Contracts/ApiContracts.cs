using ScoutHaul.Domain;

namespace ScoutHaul.Application.Contracts;

public sealed record CreateOrganizationRequest(string Name);
public sealed record AddOrganizationMemberRequest(string Email, string DisplayName, OrganizationRole Role, Guid? RoleId = null);
public sealed record OrganizationDto(Guid Id, string Name, DateTimeOffset CreatedAt);
public sealed record OrganizationMemberDto(Guid Id, Guid UserId, string Email, string DisplayName, OrganizationRole Role, Guid? RoleId, string? RoleName, string InviteStatus, DateTimeOffset JoinedAt);
public sealed record PermissionCatalogDto(IReadOnlyList<string> Permissions);
public sealed record UpsertOrganizationRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions, bool GrantsOwnerAccess);
public sealed record OrganizationRoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool IsOwnerRole, IReadOnlyList<string> Permissions);
public sealed record UpdateOrganizationMemberRoleRequest(Guid? RoleId, OrganizationRole? LegacyRole);
public sealed record UpsertOrganizationTeamRequest(string Name, string? Description);
public sealed record OrganizationTeamDto(Guid Id, Guid OrganizationId, string Name, string? Description, int MemberCount, DateTimeOffset CreatedAt);
public sealed record AddTeamMemberRequest(Guid UserId, Guid? RoleId);
public sealed record OrganizationTeamMemberDto(Guid Id, Guid TeamId, Guid UserId, string Email, string DisplayName, Guid? RoleId, string? RoleName, DateTimeOffset JoinedAt);
public sealed record CreateOrganizationInviteCodeRequest(string Name, Guid? TeamId, Guid? RoleId, DateTimeOffset? ExpiresAt, int? MaxUses);
public sealed record OrganizationInviteCodeDto(
    Guid Id,
    Guid OrganizationId,
    Guid? TeamId,
    string? TeamName,
    Guid? RoleId,
    string? RoleName,
    string Name,
    string CodePrefix,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    int? MaxUses,
    int UseCount,
    DateTimeOffset CreatedAt);
public sealed record OrganizationInviteCodeCreatedDto(OrganizationInviteCodeDto Invite, string Code);
public sealed record RedeemOrganizationInviteCodeRequest(string Code);
public sealed record OrganizationJoinResultDto(OrganizationDto Organization, OrganizationMemberDto Member, Guid? TeamId);

public sealed record SessionUserDto(Guid Id, string Email, string DisplayName);
public sealed record SessionRemoteCountsDto(
    int InventoryItems,
    int Budgets,
    int MarketObservations,
    int Listings,
    int Tasks,
    int PendingSyncOperations);
public sealed record SessionBootstrapDto(
    SessionUserDto User,
    IReadOnlyList<OrganizationDto> Organizations,
    Guid SelectedOrganizationId,
    SessionRemoteCountsDto RemoteCounts,
    DateTimeOffset ServerTime);

public sealed record UpsertProductIdentityRequest(
    string Title,
    string? Brand,
    string? Model,
    string? Category,
    string? Upc,
    string? Ean,
    string? Isbn,
    string? Sku,
    string? SearchQuery,
    string? Notes);

public sealed record ProductIdentityDto(
    Guid Id,
    string Title,
    string? Brand,
    string? Model,
    string? Category,
    string? Upc,
    string? Ean,
    string? Isbn,
    string? Sku,
    string? SearchQuery,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertInventoryItemRequest(
    Guid? ProductIdentityId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    Guid? SourceLocationId,
    string Title,
    string? Description,
    string? Category,
    string? Brand,
    string? Model,
    string? Color,
    string? Condition,
    string? SizeSystem,
    string? SizeValue,
    string? AttributesJson,
    string? MeasurementsJson,
    string? StorageLocation,
    string? Accessories,
    string? MissingParts,
    string? TestingNotes,
    string? CleaningNotes,
    string? ListingNotes,
    decimal? SellerAskingPrice,
    decimal? PurchasePrice,
    decimal? EstimatedSalePrice,
    string? Barcode,
    string? Notes);

public sealed record ChangeInventoryStatusRequest(InventoryStatus Status);

public sealed record InventoryItemDto(
    Guid Id,
    Guid? ProductIdentityId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    Guid? SourceLocationId,
    string Title,
    string? Description,
    string? Category,
    string? Brand,
    string? Model,
    string? Color,
    string? Condition,
    string? SizeSystem,
    string? SizeValue,
    string? AttributesJson,
    string? MeasurementsJson,
    string? StorageLocation,
    string? Accessories,
    string? MissingParts,
    string? TestingNotes,
    string? CleaningNotes,
    string? ListingNotes,
    InventoryStatus Status,
    decimal? SellerAskingPrice,
    decimal? PurchasePrice,
    decimal? EstimatedSalePrice,
    string? Barcode,
    string? Notes,
    IReadOnlyList<string> PhotoUrls,
    IReadOnlyList<InventoryPhotoDto> Photos,
    string? HeroPhotoThumbnailUrl,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record InventoryPhotoDto(
    Guid Id,
    Guid InventoryItemId,
    bool IsHero,
    int SortOrder,
    string OriginalUrl,
    string ThumbnailUrl,
    string ContentType,
    long OriginalByteSize,
    long ThumbnailByteSize,
    int? Width,
    int? Height,
    DateTimeOffset CreatedAt);

public sealed record InventoryPhotoUploadResultDto(
    Guid InventoryItemId,
    IReadOnlyList<string> PhotoUrls,
    string? UploadedUrl,
    IReadOnlyList<InventoryPhotoDto> Photos);

public sealed record UpsertInventoryIssueRequest(
    string IssueType,
    string Severity,
    string? Note,
    Guid? InventoryPhotoId,
    int? SortOrder,
    bool? IsResolved);

public sealed record InventoryIssueDto(
    Guid Id,
    Guid InventoryItemId,
    Guid? InventoryPhotoId,
    string IssueType,
    string Severity,
    string? Note,
    int SortOrder,
    bool IsResolved,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertMarketObservationRequest(
    Guid? ProductIdentityId,
    Guid? InventoryItemId,
    MarketObservationType ObservationType,
    string Source,
    string? SourceUrl,
    string Title,
    decimal Price,
    decimal? ShippingPrice,
    string? Currency,
    string? Condition,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? SoldAt,
    string? Notes);

public sealed record MarketObservationDto(
    Guid Id,
    Guid? ProductIdentityId,
    Guid? InventoryItemId,
    MarketObservationType ObservationType,
    string Source,
    string? SourceUrl,
    string Title,
    decimal Price,
    decimal? ShippingPrice,
    decimal TotalPrice,
    string? Currency,
    string? Condition,
    DateTimeOffset ObservedAt,
    DateTimeOffset? SoldAt,
    string? Notes);

public sealed record DealAnalysisInput(
    decimal? ExpectedSalePrice,
    decimal? SellerAskingPrice,
    decimal? PurchasePrice,
    string? Platform,
    decimal PlatformFeePercent,
    decimal PaymentFeePercent,
    decimal FixedPaymentFee,
    decimal EstimatedShippingCostPaidBySeller,
    decimal SuppliesCost,
    decimal RiskReserve,
    decimal DesiredMinimumProfit,
    decimal? TargetMarginPercent,
    int? ConfidenceScore,
    PricingBasisType PricingBasis);

public sealed record DealAnalysisResult(
    decimal? RecommendedMaxBuyPrice,
    decimal? ExpectedNetProfit,
    decimal? RoiPercent,
    decimal? MarginPercent,
    decimal? BreakEvenPrice,
    RecommendationDecisionType Decision,
    IReadOnlyList<string> Warnings);

public sealed record MarketAnalysisRequest(
    Guid? ProductIdentityId,
    Guid? InventoryItemId,
    decimal? ExpectedSalePrice,
    decimal? SellerAskingPrice,
    decimal? PurchasePrice,
    string? Platform,
    decimal PlatformFeePercent,
    decimal PaymentFeePercent,
    decimal FixedPaymentFee,
    decimal EstimatedShippingCostPaidBySeller,
    decimal SuppliesCost,
    decimal RiskReserve,
    decimal DesiredMinimumProfit,
    decimal? TargetMarginPercent,
    PricingBasisType? PricingBasis,
    string? Condition,
    IReadOnlyList<string>? RiskFlags);

public sealed record ConfidenceScoreResult(
    int Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings);

public sealed record MarketAnalysisResponse(
    Guid AnalysisId,
    Guid RecommendationId,
    Guid? ProductIdentityId,
    Guid? InventoryItemId,
    decimal? EstimatedSalePrice,
    PricingBasisType PricingBasis,
    int SoldCompCount,
    int ActiveListingCount,
    ConfidenceScoreResult Confidence,
    DealAnalysisResult Recommendation);

public sealed record BarcodeLookupResult(
    string Barcode,
    string? Title,
    string? Brand,
    string? Description,
    string? Category,
    string? ImageUrl,
    string? Upc,
    string? Ean);

public sealed record MarketplaceSearchLinkDto(string Provider, string Label, string Url);
public sealed record MarketplaceSearchLinksDto(string Query, IReadOnlyList<MarketplaceSearchLinkDto> Links);

public sealed record MarketplaceListingDto(
    string Title,
    decimal Price,
    decimal? ShippingCost,
    string? ImageUrl,
    string? ItemUrl,
    string Condition,
    string Source,
    bool IsSold,
    DateTimeOffset? SoldDate,
    // Provenance for ranking: which resale tier this came from and when it was listed (if active).
    string? MarketTier = null, // "sold-secondhand" | "active-secondhand" | "retail"
    DateTimeOffset? ListedDate = null);

public static class MarketTiers
{
    public const string SoldSecondhand = "sold-secondhand";
    public const string ActiveSecondhand = "active-secondhand";
    public const string Retail = "retail";

    /// <summary>Lower = higher priority. Sold realized comps rank above active, above retail.</summary>
    public static int Rank(string? tier) => tier switch
    {
        SoldSecondhand => 0,
        ActiveSecondhand => 1,
        Retail => 2,
        _ => 3,
    };
}

public sealed record MarketplaceLookupResult(
    string Query,
    IReadOnlyList<MarketplaceListingDto> Listings,
    decimal? AverageSoldPrice,
    decimal? LowestPrice,
    decimal? HighestPrice,
    int TotalResults,
    string? ProductTitle = null,
    string? ProductBrand = null,
    string? ProductImageUrl = null,
    string? ProductCategory = null,
    Guid? ProductIdentityId = null,
    int SavedObservationCount = 0,
    IReadOnlyList<string>? ProvidersUsed = null,
    IReadOnlyList<string>? ProviderWarnings = null,
    // AI resale estimate — filled when no live marketplace data is available, so barcode and
    // keyword lookups still surface a value (parity with photo identification).
    decimal? EstimatedResaleLow = null,
    decimal? EstimatedResaleHigh = null,
    string? EstimateSource = null,
    string? EstimateRationale = null,
    string? EstimatePriceRangeType = null,
    string? EstimateValuationConfidence = null,
    IReadOnlyList<ResaleComp>? EstimateComps = null,
    IReadOnlyList<ResaleSource>? EstimateSources = null);

/// <summary>AI-generated used-resale price estimate for a product (text-only, no live comps).</summary>
public sealed record ResaleEstimate(
    decimal Low,
    decimal High,
    string? Rationale,
    string? ValuationConfidence = null,
    string? PriceRangeType = null,
    IReadOnlyList<string>? KeyValueFactors = null,
    IReadOnlyList<string>? MissingInformation = null,
    IReadOnlyList<string>? RecommendedNextSteps = null,
    IReadOnlyList<ResaleComp>? Comps = null,
    IReadOnlyList<ResaleSource>? Sources = null);

/// <summary>
/// A comparable listing found via Google Search grounding — a REAL recent sold/active comp.
/// </summary>
public sealed record ResaleComp(
    decimal Price,
    string Status,      // "sold" | "active"
    string? Condition,
    string? Note,
    string? SourceUrl = null);

/// <summary>A web source the grounded search used (for "verify"/"open listing" links).</summary>
public sealed record ResaleSource(string Url, string? Title);

public sealed record CreateSourcingTripRequest(
    string Name,
    DateTimeOffset? StartedAt,
    DateOnly? RouteDate,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    SourcingRouteType? RouteType,
    decimal? CashBudget,
    Guid? AssignedToUserId,
    string? Notes);

public sealed record UpdateSourcingTripRequest(
    string Name,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateOnly? RouteDate,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    SourcingRouteType? RouteType,
    SourcingRouteStatus? Status,
    decimal? CashBudget,
    Guid? AssignedToUserId,
    string? Notes);

public sealed record AddItemToTripRequest(Guid InventoryItemId, Guid? SourcingTripStopId);
public sealed record UpdateSourcingRouteStatusRequest(SourcingRouteStatus Status);

public sealed record UpsertSourcingTripStopRequest(
    string Name,
    SourcingStopSourceType SourceType,
    string Address,
    string? Phone,
    string? Url,
    string? Notes,
    int? PlannedOrder,
    DateTimeOffset? PlannedArrivalAt,
    int EstimatedDurationMinutes,
    SourcingStopPriority Priority,
    bool IsTimeSensitive,
    DateTimeOffset? TimeWindowStartAt,
    DateTimeOffset? TimeWindowEndAt,
    bool CanBeSkipped,
    decimal? BudgetCap);

public sealed record ReorderSourcingTripStopsRequest(IReadOnlyList<Guid> StopIds);
public sealed record CompleteSourcingStopRequest(bool Force);
public sealed record SkipSourcingStopRequest(string? Reason);
public sealed record UpdateChecklistItemRequest(bool IsCompleted);
public sealed record CreateChecklistItemRequest(string Text, bool IsRequired);

public sealed record SourcingStopChecklistItemDto(
    Guid Id,
    Guid SourcingTripStopId,
    string Text,
    int SortOrder,
    bool IsRequired,
    bool IsCompleted,
    DateTimeOffset? CompletedAt);

public sealed record SourcingTripStopDto(
    Guid Id,
    Guid SourcingTripId,
    string Name,
    SourcingStopSourceType SourceType,
    string Address,
    string? Phone,
    string? Url,
    string? Notes,
    int PlannedOrder,
    DateTimeOffset? PlannedArrivalAt,
    int EstimatedDurationMinutes,
    SourcingStopPriority Priority,
    bool IsTimeSensitive,
    DateTimeOffset? TimeWindowStartAt,
    DateTimeOffset? TimeWindowEndAt,
    bool CanBeSkipped,
    decimal? BudgetCap,
    SourcingStopStatus Status,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt,
    decimal TotalPurchaseCost,
    int PurchaseCount,
    IReadOnlyList<SourcingStopChecklistItemDto> ChecklistItems,
    DateTimeOffset CreatedAt);

public sealed record SourcingTripDto(
    Guid Id,
    Guid? AssignedToUserId,
    string Name,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateOnly? RouteDate,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    SourcingRouteType RouteType,
    SourcingRouteStatus Status,
    decimal? CashBudget,
    string? Notes,
    int ItemCount,
    int StopCount,
    DateTimeOffset CreatedAt);

public sealed record SourcingTripSummaryDto(
    Guid Id,
    string Name,
    int ItemCount,
    int StopCount,
    int CompletedStopCount,
    int SkippedStopCount,
    int PurchaseCount,
    decimal TotalPurchaseCost,
    decimal TotalAskingPrice,
    decimal? CashBudget,
    decimal RemainingBudget,
    int PlannedMinutes,
    int ActualMinutes,
    int ScheduleVarianceMinutes,
    SourcingRouteStatus Status);

public sealed record SourcingRouteDetailDto(
    SourcingTripDto Trip,
    SourcingTripSummaryDto Summary,
    IReadOnlyList<SourcingTripStopDto> Stops);

public sealed record CreateBudgetRequest(string Name, decimal StartingAmount, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);
public sealed record BudgetDto(Guid Id, string Name, decimal StartingAmount, decimal CurrentAmount, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool IsActive);
public sealed record AddExpenseRequest(Guid? InventoryItemId, Guid? SourcingTripId, Guid? SourcingTripStopId, ExpenseCategory Category, string Description, decimal Amount, DateTimeOffset? OccurredAt);
public sealed record ExpenseDto(Guid Id, Guid? InventoryItemId, Guid? SourcingTripId, Guid? SourcingTripStopId, ExpenseCategory Category, string Description, decimal Amount, DateTimeOffset OccurredAt);
public sealed record BudgetSummaryDto(Guid? BudgetId, decimal StartingAmount, decimal CurrentAmount, decimal TotalExpenses, decimal InventoryPurchases, decimal RemainingAmount);

public sealed record CreateListingRequest(Guid InventoryItemId, string Platform, string? ListingUrl, decimal ListPrice, string? Notes);
public sealed record UpdateListingRequest(string Platform, string? ListingUrl, decimal ListPrice, ListingStatus Status, string? Notes);
public sealed record ListingDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    InventoryStatus InventoryItemStatus,
    string Platform,
    string? ListingUrl,
    decimal ListPrice,
    ListingStatus Status,
    DateTimeOffset? ListedAt,
    DateTimeOffset? EndedAt,
    string? Notes);
public sealed record RecordSaleRequest(Guid InventoryItemId, Guid? ListingId, string Platform, decimal SoldPrice, decimal PlatformFees, decimal PaymentFees, decimal ShippingCost, decimal SuppliesCost, decimal OtherCosts, DateTimeOffset? SoldAt, string? Notes);
public sealed record SaleDto(Guid Id, Guid InventoryItemId, string InventoryItemTitle, Guid? ListingId, string Platform, decimal SoldPrice, decimal PlatformFees, decimal PaymentFees, decimal ShippingCost, decimal SuppliesCost, decimal OtherCosts, decimal NetProfit, SaleStatus Status, DateTimeOffset SoldAt, string? Notes);
public sealed record RealizedProfitDto(Guid SaleId, decimal SoldPrice, decimal PurchaseCost, decimal TotalFeesAndCosts, decimal NetProfit, decimal RoiPercent, decimal MarginPercent);

public sealed record CreateTaskRequest(Guid? InventoryItemId, Guid? AssignedToUserId, string Title, string? Description, DateTimeOffset? DueAt);
public sealed record AssignTaskRequest(Guid? AssignedToUserId);
public sealed record UpdateTaskStatusRequest(WorkTaskStatus Status);
public sealed record ResaleTaskDto(Guid Id, Guid? InventoryItemId, Guid? AssignedToUserId, string Title, string? Description, WorkTaskStatus Status, DateTimeOffset? DueAt, DateTimeOffset CreatedAt);

public sealed record DashboardSummaryDto(
    int CurrentInventoryCount,
    int ActiveListingsCount,
    decimal TotalInventoryCost,
    decimal EstimatedInventoryValue,
    decimal GrossSales,
    decimal NetProfit,
    decimal CashDeployed,
    int UnlistedInventoryCount,
    int StaleInventoryCount);

// ── Vision Identification ────────────────────────────────────────

public sealed record VisionIdentifyRequest(
    string? ImageBase64,
    string? ImageUrl,
    // Optional known facts the user already entered (brand, condition, size, notes, …). Fed to the
    // model to sharpen identification + valuation. Null/absent → behaves exactly as before (so older
    // clients that don't send it are unaffected).
    string? Context = null);

public sealed record VisionIdentifyResult(
    ProductIdentification? Product,
    string? SearchQuery,
    string? DetectedText);

/// <summary>
/// Structured product identification + valuation from Gemini multimodal — the sole identifier.
/// </summary>
public sealed record ProductIdentification(
    string? Name,
    string? Brand,
    string? Model,
    string? Category,
    string? Color,
    string? Condition,
    decimal? EstimatedResaleLow,
    decimal? EstimatedResaleHigh,
    string? IdentifyingText,
    IReadOnlyList<string> SearchKeywords,
    string Confidence, // identification (variant) confidence — kept name for compatibility
    string? ValuationConfidence = null,
    string? PriceRangeType = null, // tight | moderate | wide
    IReadOnlyList<string>? KeyValueFactors = null,
    IReadOnlyList<string>? MissingInformation = null,
    IReadOnlyList<string>? RecommendedNextSteps = null,
    string? Rationale = null,
    IReadOnlyList<ResaleComp>? Comps = null,
    // Rough condition-quality rating inferred from the photo: new | like-new | good | fair | poor |
    // for-parts. Distinct from the free-text Condition. Additive — null when not assessed.
    string? ConditionQuality = null);
