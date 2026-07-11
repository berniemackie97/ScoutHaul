using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

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
    Guid? SelectedOrganizationId,
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
    string? Notes,
    InventoryOwnershipType? OwnershipType = null,
    InventoryFunctionalStatus? FunctionalStatus = null,
    InventoryCompletenessStatus? CompletenessStatus = null,
    InventoryAuthenticityStatus? AuthenticityStatus = null,
    InventoryPackagingStatus? PackagingStatus = null,
    string? ManufacturerPartNumber = null,
    string? SerialNumber = null,
    string? CountryOfOrigin = null,
    int? ManufactureYear = null,
    decimal? Length = null,
    decimal? Width = null,
    decimal? Height = null,
    DimensionUnit? DimensionUnit = null,
    decimal? Weight = null,
    WeightUnit? WeightUnit = null,
    InventoryStatus? Status = null);

public sealed record ChangeInventoryStatusRequest(InventoryStatus Status);

public sealed record InventoryReferenceOptionDto(
    string Value,
    string Label,
    string? Group = null,
    string? ParentValue = null,
    bool IsLegacy = false);

public sealed record InventoryReferenceDataDto(
    IReadOnlyList<InventoryReferenceOptionDto> Categories,
    IReadOnlyList<InventoryReferenceOptionDto> Brands,
    IReadOnlyList<InventoryReferenceOptionDto> Models,
    IReadOnlyList<InventoryReferenceOptionDto> Conditions,
    IReadOnlyList<InventoryReferenceOptionDto> Colors,
    IReadOnlyList<InventoryReferenceOptionDto> SizeSystems,
    IReadOnlyList<InventoryReferenceOptionDto> Sizes,
    IReadOnlyList<InventoryReferenceOptionDto> StorageLocations,
    IReadOnlyList<InventoryReferenceOptionDto> Countries);

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
    bool IsBundle,
    int BundleComponentCount,
    IReadOnlyList<InventoryBundleComponentDto> BundleComponents,
    Guid? AcquisitionLotId,
    string? AcquisitionLotName,
    decimal? AcquisitionAllocatedCost,
    InventoryLifecycleGroup LifecycleGroup,
    bool IsSold,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    InventoryOwnershipType OwnershipType,
    InventoryFunctionalStatus FunctionalStatus,
    InventoryCompletenessStatus CompletenessStatus,
    InventoryAuthenticityStatus AuthenticityStatus,
    InventoryPackagingStatus PackagingStatus,
    string? ManufacturerPartNumber,
    string? SerialNumber,
    string? CountryOfOrigin,
    int? ManufactureYear,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    DimensionUnit DimensionUnit,
    decimal? Weight,
    WeightUnit WeightUnit);

/// <summary>Aggregate over a filtered inventory set (whole result, not just the page) for the inventory KPIs.</summary>
public sealed record InventorySummaryDto(
    int Total,
    int ActiveItems,
    int ReadyOrListed,
    int Sold,
    decimal CapitalDeployed,
    decimal EstimatedResaleValue);

/// <summary>A searchable, sortable, paged page of inventory plus the summary over the whole filtered set.</summary>
public sealed record InventoryListDto(IReadOnlyList<InventoryItemDto> Items, int Total, int Page, int PageSize, int PageCount, InventorySummaryDto Summary);

public sealed record AcquisitionLotItemInputDto(
    Guid InventoryItemId,
    decimal? ManualAllocatedCost = null,
    decimal? AllocationWeight = null,
    string? Notes = null);

public sealed record UpsertAcquisitionLotRequest(
    string Name,
    DateTimeOffset? PurchasedAt,
    decimal PurchaseSubtotal,
    decimal ShippingCost,
    decimal TaxCost,
    decimal FeeCost,
    decimal OtherCost,
    AcquisitionLotAllocationMethod AllocationMethod,
    string? SupplierName,
    Guid? SourceLocationId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    string? ExternalReference,
    string? Notes,
    IReadOnlyList<AcquisitionLotItemInputDto> Items);

public sealed record AcquisitionLotItemDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    InventoryStatus InventoryItemStatus,
    decimal AllocatedCost,
    decimal? AllocationWeight,
    string? Notes);

/// <summary>A new inventory item to create directly inside an acquisition lot ("itemize the lot").</summary>
public sealed record NewLotItemDto(
    string Title,
    string? Category = null,
    string? Brand = null,
    string? Condition = null,
    decimal? EstimatedSalePrice = null,
    decimal? ManualAllocatedCost = null,
    decimal? AllocationWeight = null,
    string? StorageLocation = null,
    string? Notes = null);

/// <summary>Creates draft inventory items inside an existing lot, then re-allocates landed cost across all items.</summary>
public sealed record AddLotItemsRequest(IReadOnlyList<NewLotItemDto> Items);

public sealed record AcquisitionLotDto(
    Guid Id,
    string Name,
    string? SupplierName,
    Guid? SourceLocationId,
    Guid? SourcingTripId,
    Guid? SourcingTripStopId,
    DateTimeOffset PurchasedAt,
    decimal PurchaseSubtotal,
    decimal ShippingCost,
    decimal TaxCost,
    decimal FeeCost,
    decimal OtherCost,
    decimal TotalLandedCost,
    decimal TotalAllocatedCost,
    AcquisitionLotAllocationMethod AllocationMethod,
    string? ExternalReference,
    string? Notes,
    IReadOnlyList<AcquisitionLotItemDto> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateInventoryBundleRequest(
    string Title,
    string? Description,
    string? Category,
    decimal? EstimatedSalePrice,
    InventoryStatus? Status,
    IReadOnlyList<Guid> ComponentInventoryItemIds,
    string? Notes);

public sealed record InventoryBundleComponentDto(
    Guid Id,
    Guid BundleInventoryItemId,
    Guid ComponentInventoryItemId,
    string ComponentTitle,
    InventoryStatus ComponentStatus,
    int Quantity,
    decimal CostSnapshot,
    string? Notes);

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
public sealed record RecordSaleRequest(
    Guid InventoryItemId,
    Guid? ListingId,
    string Platform,
    decimal SoldPrice,
    decimal PlatformFees,
    decimal PaymentFees,
    decimal ShippingCost,
    decimal SuppliesCost,
    decimal OtherCosts,
    DateTimeOffset? SoldAt,
    string? Notes,
    decimal SalesTaxCollected = 0,
    bool TaxRemittedByPlatform = true,
    decimal ShippingCharged = 0,
    string? ExternalOrderId = null,
    string? BuyerReference = null,
    string? BuyerName = null,
    string? ShippingCarrier = null,
    string? TrackingNumber = null);
/// <summary>Edits an existing sale. <c>Reason</c> is required and recorded with a before/after audit snapshot.</summary>
public sealed record UpdateSaleRequest(string Platform, decimal SoldPrice, decimal ShippingCharged, decimal PlatformFees, decimal PaymentFees, decimal ShippingCost, decimal SuppliesCost, decimal OtherCosts, decimal SalesTaxCollected, bool TaxRemittedByPlatform, DateTimeOffset SoldAt, string? Notes, string Reason);
public sealed record SaleStatusTransitionRequest(SaleStatus Status, DateTimeOffset? OccurredAt, string? Reason, string? Carrier, string? TrackingNumber);
public sealed record ChannelFeeRuleDto(
    string Name,
    decimal Percent = 0,
    decimal FixedAmount = 0,
    decimal? AppliesAboveAmount = null,
    decimal? AppliesUpToAmount = null,
    decimal? MinimumFee = null,
    decimal? MaximumFee = null,
    string? Notes = null);

public sealed record ChannelFeeProfileDto(
    string Id,
    string Label,
    string Description,
    bool IsDefault,
    IReadOnlyList<ChannelFeeRuleDto> PlatformRules,
    IReadOnlyList<ChannelFeeRuleDto> PaymentRules);

/// <summary>
/// A controlled sales channel plus selectable fee profiles. Profiles are estimates only; the recorded sale
/// stores the actual assessed platform/payment fees so reconciliation can correct marketplace differences.
/// </summary>
public sealed record SaleChannelOptionDto(
    string Value,
    string Label,
    string Group,
    bool IsLegacy = false,
    IReadOnlyList<ChannelFeeProfileDto>? FeeProfiles = null);
public sealed record SalesReferenceDataDto(IReadOnlyList<SaleChannelOptionDto> Channels);
public sealed record SaleCandidateDto(Guid InventoryItemId, string Title, InventoryStatus Status, decimal CostOfGoods, decimal? ExpectedSalePrice, Guid? ListingId, string? ListingPlatform, decimal? ListingPrice);
public sealed record EstimateChannelFeesRequest(string Platform, string FeeProfileId, decimal SoldPrice);
public sealed record ChannelFeeEstimateDto(string Platform, string FeeProfileId, string FeeProfileLabel, decimal SoldPrice, decimal EstimatedPlatformFees, decimal EstimatedPaymentFees, decimal EstimatedTotalFees, string? Notes);
public sealed record SaleShipmentDto(Guid Id, Guid SaleId, string? Carrier, string? TrackingNumber, decimal? Cost, DateTimeOffset? PackedAt, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt);
public sealed record SaleDto(Guid Id, Guid InventoryItemId, string InventoryItemTitle, Guid? ListingId, string Platform, decimal SoldPrice, decimal ShippingCharged, decimal CostOfGoods, decimal PlatformFees, decimal PaymentFees, decimal ShippingCost, decimal SuppliesCost, decimal OtherCosts, decimal SalesTaxCollected, bool TaxRemittedByPlatform, decimal SalesTaxOwed, decimal NetProfit, SaleStatus Status, DateTimeOffset SoldAt, string? Notes, IReadOnlyList<SaleStatus> AllowedTransitions, Guid? SaleGroupId = null, string? GroupOrderReference = null, decimal TotalRefunded = 0, decimal FeeCreditTotal = 0, int RefundCount = 0, Guid? OrderId = null);

/// <summary>Aggregate P&amp;L over a filtered set of sales (realized only) — powers the finance KPIs so they reflect
/// the whole filtered result, not just the current page.</summary>
public sealed record SalesSummaryDto(
    int RealizedCount,
    decimal ItemSales,
    decimal BuyerShipping,
    decimal SalesTaxCollected,
    decimal GrossBuyerSales,
    decimal PlatformFees,
    decimal PaymentFees,
    decimal ShippingLabels,
    decimal SuppliesAndOther,
    decimal SellingCosts,
    decimal MarketplaceNetSales,
    decimal CostOfGoods,
    decimal NetProfit,
    decimal SalesTaxOwed,
    decimal TotalRefunded);

/// <summary>A searchable, sortable, paged page of sales plus the summary over the whole filtered set.</summary>
public sealed record SaleListDto(IReadOnlyList<SaleDto> Items, int Total, int Page, int PageSize, int PageCount, SalesSummaryDto Summary);

/// <summary>Record a refund (full or partial) against a sale. Item amount + buyer shipping reduce revenue; the
/// platform's fee credit reduces selling cost; tax refunded is pass-through. Net profit is recomputed + audited.</summary>
public sealed record RecordRefundRequest(
    RefundType Type,
    decimal ItemAmount,
    string Reason,
    decimal ShippingRefunded = 0,
    decimal TaxRefunded = 0,
    decimal FeeCredit = 0,
    bool ItemReturned = false,
    DateTimeOffset? RefundedAt = null,
    string? Notes = null);

public sealed record RefundDto(
    Guid Id,
    Guid SaleId,
    RefundType Type,
    decimal ItemAmount,
    decimal ShippingRefunded,
    decimal TaxRefunded,
    decimal FeeCredit,
    bool ItemReturned,
    string Reason,
    string? Notes,
    DateTimeOffset RefundedAt,
    string RecordedBy);

// ── Returns (RMA lifecycle — distinct from the Refund money event) ──────────

public sealed record ReturnDto(
    Guid Id,
    Guid SaleId,
    Guid? OrderId,
    string InventoryItemTitle,
    ReturnStatus Status,
    decimal ReturnShippingCost,
    string Reason,
    string? Notes,
    bool Restocked,
    Guid? RefundId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? LabelIssuedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? ResolvedAt);

public sealed record OpenReturnRequest(string Reason, string? Notes = null);

/// <summary>Issues the buyer's return label; the cost is booked against the sale immediately.</summary>
public sealed record IssueReturnLabelRequest(decimal ReturnShippingCost, string? Notes = null);

/// <summary>Received + inspected → issue the refund. Restock puts a resellable item back into inventory (frees COGS).</summary>
public sealed record ResolveReturnRequest(
    decimal ItemAmount,
    RefundType Type = RefundType.Full,
    decimal ShippingRefunded = 0,
    decimal TaxRefunded = 0,
    decimal FeeCredit = 0,
    bool Restock = true,
    string? Reason = null,
    string? Notes = null);

public sealed record RejectReturnRequest(string Reason);

public sealed record BundledSaleItemInput(Guid InventoryItemId, decimal? ManualPrice = null);
public sealed record RecordBundledSaleRequest(
    IReadOnlyList<BundledSaleItemInput> Items,
    string Platform,
    decimal TotalSoldPrice,
    decimal ShippingCharged,
    decimal PlatformFees,
    decimal PaymentFees,
    decimal ShippingCost,
    decimal SuppliesCost,
    decimal OtherCosts,
    decimal SalesTaxCollected,
    bool TaxRemittedByPlatform,
    DateTimeOffset? SoldAt,
    string? OrderReference,
    string? Notes);
public sealed record BundledSaleResultDto(Guid SaleGroupId, string? OrderReference, int ItemCount, decimal TotalNetProfit, IReadOnlyList<SaleDto> Sales);

/// <summary>Combine several already-recorded sales into one marketplace order (shared group + order reference),
/// the way eBay groups a multi-item order. Each sale keeps its own allocated price, fees, and net.</summary>
public sealed record GroupSalesRequest(IReadOnlyList<Guid> SaleIds, string? OrderReference = null);
public sealed record UngroupSalesRequest(IReadOnlyList<Guid> SaleIds);
public sealed record SaleGroupResultDto(Guid SaleGroupId, string? OrderReference, IReadOnlyList<SaleDto> Sales);

public sealed record SaleCorrectionDto(Guid Id, Guid SaleId, int Version, string Reason, string CorrectedBy, DateTimeOffset CorrectedAt, string BeforeJson, string AfterJson);
public sealed record RealizedProfitDto(Guid SaleId, decimal SoldPrice, decimal ShippingCharged, decimal SalesRevenue, decimal PurchaseCost, decimal SellingCosts, decimal TotalFeesAndCosts, decimal MarketplaceNetBeforeCogs, decimal SalesTaxOwed, decimal NetProfit, decimal RoiPercent, decimal MarginPercent);

// --- Platform order ingestion (marketplace reports / manual imports → reconciled into Sales) ---

/// <summary>One line of an imported marketplace order, exactly as the platform reports it.</summary>
public sealed record IngestPlatformOrderLineInput(
    string Title,
    decimal UnitPrice,
    int Quantity = 1,
    string? ExternalItemId = null,
    string? Sku = null,
    decimal? ShippingCharged = null,
    decimal? TaxCollected = null,
    decimal? PlatformFees = null,
    decimal? SellerShippingCost = null,
    decimal? NetSales = null,
    decimal? TotalSales = null,
    string? FeeBreakdownJson = null,
    string? RawJson = null);

/// <summary>One imported marketplace order with the platform's own money figures. <see cref="ExternalOrderId"/>
/// is the idempotency key: re-ingesting the same order updates it in place (until it is reconciled).</summary>
public sealed record IngestPlatformOrderInput(
    string ExternalOrderId,
    DateTimeOffset OrderedAt,
    decimal ItemsSubtotal,
    decimal ShippingCharged,
    decimal TaxCollected,
    decimal FeeTotal,
    IReadOnlyList<IngestPlatformOrderLineInput> Lines,
    string? Platform = null,
    decimal? ShippingCost = null,
    decimal? PayoutAmount = null,
    string Currency = "USD",
    bool TaxRemittedByPlatform = true,
    string? BuyerName = null,
    string? BuyerReference = null,
    string? BuyerEmail = null,
    string? BuyerNote = null,
    string? ShipToName = null,
    string? ShipToPhone = null,
    string? ShipToLine1 = null,
    string? ShipToLine2 = null,
    string? ShipToCity = null,
    string? ShipToRegion = null,
    string? ShipToPostalCode = null,
    string? ShipToCountry = null,
    string? Carrier = null,
    string? TrackingNumber = null,
    DateTimeOffset? PaidAt = null,
    DateTimeOffset? ShippedAt = null,
    string? FeeBreakdownJson = null,
    string? RawJson = null);

/// <summary>A batch of imported orders. <see cref="Platform"/> is the default channel for
/// orders that do not carry their own.</summary>
public sealed record IngestPlatformOrdersRequest(
    string Platform,
    IReadOnlyList<IngestPlatformOrderInput> Orders,
    Guid? ChannelAccountId = null);

public sealed record PlatformOrderLineDto(
    Guid Id,
    string Title,
    decimal UnitPrice,
    int Quantity,
    string? ExternalItemId,
    string? Sku,
    decimal? ShippingCharged,
    decimal? TaxCollected,
    decimal? PlatformFees,
    decimal? SellerShippingCost,
    decimal? NetSales,
    decimal? TotalSales,
    string? FeeBreakdownJson,
    Guid? InventoryItemId,
    string? InventoryItemTitle,
    Guid? SuggestedInventoryItemId,
    string? SuggestedInventoryItemTitle,
    bool SuggestedInventoryItemAlreadySold);

public sealed record PlatformOrderDto(
    Guid Id,
    string Platform,
    string ExternalOrderId,
    string? BuyerName,
    string? BuyerReference,
    string? BuyerEmail,
    string? BuyerNote,
    string? ShipToName,
    string? ShipToPhone,
    string? ShipToLine1,
    string? ShipToLine2,
    string? ShipToCity,
    string? ShipToRegion,
    string? ShipToPostalCode,
    string? ShipToCountry,
    DateTimeOffset OrderedAt,
    decimal ItemsSubtotal,
    decimal ShippingCharged,
    decimal TaxCollected,
    decimal FeeTotal,
    decimal? SellerShippingCost,
    decimal? PayoutAmount,
    decimal ExpectedPayout,
    decimal PayoutVariance,
    string Currency,
    bool TaxRemittedByPlatform,
    string? FeeBreakdownJson,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ShippedAt,
    PlatformOrderStatus Status,
    Guid? ReconciledSaleGroupId,
    DateTimeOffset? ReconciledAt,
    DateTimeOffset OrderedAtUtc,
    DateTimeOffset ScrapedAt,
    IReadOnlyList<PlatformOrderLineDto> Lines);

public sealed record IngestPlatformOrdersResultDto(int Created, int Updated, int Skipped, IReadOnlyList<PlatformOrderDto> Orders);

/// <summary>Import a marketplace's own CSV export (e.g. eBay Seller Hub → Performance → Sales report) — the
/// authoritative fee source. Parsed server-side into the same idempotent ingest path as manual JSON.</summary>
public sealed record ImportPlatformOrdersCsvRequest(string Platform, string Csv);

/// <summary>Import a marketplace's own listings export (eBay Active Listings today) — parsed server-side into the
/// same idempotent listing ingest, so legacy listings seed/link inventory without re-entry.</summary>
public sealed record ImportPlatformListingsCsvRequest(string Platform, string Csv);

/// <summary>Import marketplace refund rows from seller exports. The importer posts only exact sale matches and
/// reports ambiguous rows as skipped warnings.</summary>
public sealed record ImportMarketplaceRefundsCsvRequest(string Platform, string Csv);

public sealed record ImportMarketplaceRefundsResultDto(int Imported, int Skipped, IReadOnlyList<string> Warnings);

/// <summary>Maps one imported order line to the ScoutHaul inventory item it represents, for reconciliation.</summary>
public sealed record ReconcilePlatformOrderLineMap(Guid PlatformOrderLineId, Guid InventoryItemId);

/// <summary>Turns an imported order into ScoutHaul <see cref="SaleDto"/>(s) using the platform's exact money figures.</summary>
public sealed record ReconcilePlatformOrderRequest(
    IReadOnlyList<ReconcilePlatformOrderLineMap> Lines,
    string? Notes = null);

public sealed record PlatformOrderReconcileResultDto(Guid PlatformOrderId, Guid SaleGroupId, int CreatedSaleCount, int CorrectedSaleCount, IReadOnlyList<SaleDto> Sales);

// --- Platform listing ingestion (captured/imported marketplace listings → seed/link inventory) ---

/// <summary>One captured or imported marketplace listing in any state. <see cref="ExternalListingId"/> is the idempotency key.</summary>
public sealed record IngestPlatformListingInput(
    string ExternalListingId,
    string Title,
    decimal Price,
    PlatformListingStatus Status,
    int Quantity = 1,
    int? QuantitySold = null,
    string? Platform = null,
    string Currency = "USD",
    string? Format = null,
    string? Sku = null,
    string? CategoryName = null,
    string? Condition = null,
    string? Url = null,
    string? PrimaryImageUrl = null,
    DateTimeOffset? ListedAt = null,
    DateTimeOffset? EndsAt = null,
    string? RawJson = null);

public sealed record IngestPlatformListingsRequest(
    string Platform,
    IReadOnlyList<IngestPlatformListingInput> Listings,
    Guid? ChannelAccountId = null);

public sealed record PlatformListingDto(
    Guid Id,
    string Platform,
    string ExternalListingId,
    string Title,
    decimal Price,
    int Quantity,
    int? QuantitySold,
    string Currency,
    PlatformListingStatus Status,
    string? Format,
    string? Sku,
    string? CategoryName,
    string? Condition,
    string? Url,
    string? PrimaryImageUrl,
    DateTimeOffset? ListedAt,
    DateTimeOffset? EndsAt,
    PlatformListingReconcileStatus ReconcileStatus,
    Guid? ReconciledInventoryItemId,
    string? ReconciledInventoryItemTitle,
    Guid? SuggestedInventoryItemId,
    string? SuggestedInventoryItemTitle,
    bool SuggestedInventoryItemAlreadySold,
    DateTimeOffset ScrapedAt);

public sealed record IngestPlatformListingsResultDto(int Created, int Updated, int Skipped, IReadOnlyList<PlatformListingDto> Listings);

/// <summary>Turn an imported listing into ScoutHaul inventory: link it to an existing item, or create a new draft
/// item seeded from the listing. Either way a <c>ChannelListing</c> is created/updated so the item shows on eBay.</summary>
public sealed record ReconcilePlatformListingRequest(Guid? InventoryItemId = null, bool CreateNewItem = false);

public sealed record PlatformListingReconcileResultDto(
    Guid PlatformListingId,
    Guid InventoryItemId,
    Guid ChannelListingId,
    bool CreatedInventoryItem,
    PlatformListingDto Listing);

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

public sealed record ReportKpiDto(
    int SalesCount,
    int ReturnedSalesCount,
    decimal GrossSales,
    decimal CostOfGoods,
    decimal GrossProfit,
    decimal SaleCosts,
    decimal RealizedNetProfit,
    decimal OperatingExpenses,
    decimal InventorySpend,
    decimal AverageSaleValue,
    decimal NetMarginPercent,
    decimal SalesTaxOwed,
    decimal NetIncomeAfterExpenses);

public sealed record ReportChangeDto(decimal Current, decimal Previous, decimal? PercentChange);
public sealed record ReportComparisonDto(ReportChangeDto GrossSales, ReportChangeDto NetProfit, ReportChangeDto SalesCount, ReportChangeDto AverageSaleValue, ReportChangeDto OperatingExpenses);
public sealed record ReportSeriesPointDto(DateTimeOffset PeriodStart, string Label, decimal GrossSales, decimal NetProfit, decimal OperatingExpenses, decimal InventorySpend, int SalesCount);
public sealed record ReportDimensionDto(string Name, int Count, decimal Amount, decimal SecondaryAmount);
public sealed record ReportAgingBucketDto(string Label, int Count, decimal Cost, decimal EstimatedValue);
public sealed record ReportSaleRowDto(Guid SaleId, Guid InventoryItemId, string Title, string Platform, decimal SoldPrice, decimal PurchaseCost, decimal NetProfit, decimal MarginPercent, DateTimeOffset SoldAt);
public sealed record ReportInventorySnapshotDto(int ItemCount, decimal Cost, decimal EstimatedValue, int UnlistedCount, int StaleCount, int MissingCostCount, int MissingValueCount, int MissingLocationCount, int MissingPhotoCount);
public sealed record BusinessReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    string Granularity,
    DateTimeOffset GeneratedAt,
    ReportKpiDto Kpis,
    ReportComparisonDto Comparison,
    ReportInventorySnapshotDto Inventory,
    IReadOnlyList<ReportSeriesPointDto> Series,
    IReadOnlyList<ReportDimensionDto> Platforms,
    IReadOnlyList<ReportDimensionDto> ExpenseCategories,
    IReadOnlyList<ReportDimensionDto> InventoryCategories,
    IReadOnlyList<ReportDimensionDto> InventoryStatuses,
    IReadOnlyList<ReportAgingBucketDto> Aging,
    IReadOnlyList<ReportSaleRowDto> TopSales);

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
/// Structured product identification + valuation from Gemini — the sole identifier/appraiser for photo or typed details.
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

public sealed record GenerateListingRequest(
    string Title,
    string? Brand = null,
    string? Model = null,
    string? Category = null,
    string? Condition = null,
    string? Color = null,
    string? SizeSystem = null,
    string? SizeValue = null,
    string? AttributesJson = null,
    string? Notes = null);

/// <summary>
/// A ready-to-post listing generated from an item's details: title + description copy, plus a
/// competitive suggested list price backed by real sold/active comps and the resale range.
/// </summary>
public sealed record GeneratedListingDto(
    string ListingTitle,
    string ListingDescription,
    decimal? SuggestedListPrice,
    decimal? EstimatedResaleLow,
    decimal? EstimatedResaleHigh,
    string? PriceRationale,
    IReadOnlyList<ResaleComp> Comps);

// ── Storefront orders ──────────────────────────────────────────────────────
public sealed record OrderLineDto(
    Guid Id,
    Guid InventoryItemId,
    string TitleSnapshot,
    string? SkuSnapshot,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    decimal? UnitCostSnapshot,
    SaleDto? Sale = null);

public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    string? Email,
    string? CustomerName,
    Guid? CustomerId,
    decimal GrandTotal,
    string Currency,
    int ItemCount,
    bool IsLocalPickup,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? ShippingCarrier,
    string? TrackingNumber,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    string Channel = "Storefront",
    string? ExternalOrderId = null,
    string? BuyerReference = null);

public sealed record OrderDetailDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfillmentStatus FulfillmentStatus,
    string? Email,
    string? CustomerName,
    Guid? CustomerId,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal ShippingTotal,
    decimal GrandTotal,
    string Currency,
    decimal CostOfGoods,
    decimal GrossMargin,
    string? ShipToName,
    string? ShipToLine1,
    string? ShipToLine2,
    string? ShipToCity,
    string? ShipToRegion,
    string? ShipToPostalCode,
    string? ShipToCountry,
    bool IsLocalPickup,
    string? CustomerNote,
    string? ShippingCarrier,
    string? TrackingNumber,
    string? TrackingUrl,
    string? StripePaymentIntentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<OrderLineDto> Lines,
    string Channel = "Storefront",
    string? ExternalOrderId = null,
    string? ExternalOrderUrl = null,
    string? BuyerReference = null,
    // Buyer money refunded across every line of the order (item + shipping). Net received = GrandTotal − TotalRefunded.
    decimal TotalRefunded = 0);

public sealed record UpdateOrderStatusRequest(OrderStatus Status);

/// <summary>Combine several orders into one (e.g. items a buyer purchased together but recorded as separate sales).
/// The lines and sales move onto a single surviving order; the emptied orders are removed. When
/// <see cref="SurvivingOrderId"/> is null the oldest order in the set survives so its original number/history is kept.</summary>
public sealed record MergeOrdersRequest(IReadOnlyList<Guid> OrderIds, Guid? SurvivingOrderId = null);

public sealed record UpdateOrderTrackingRequest(
    string? ShippingCarrier,
    string? TrackingNumber,
    string? TrackingUrl,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    bool MarkShipped,
    bool MarkDelivered);

// ── Customers (every channel, not just the storefront) ─────────────────────

/// <summary>How a customer is known on one channel — a storefront email, or an eBay/Depop/Poshmark username.</summary>
public sealed record CustomerChannelIdentityDto(
    Guid Id,
    string Channel,
    string ExternalUserId,
    string? DisplayName,
    string? Email,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record CustomerAddressDto(
    Guid Id,
    string? Label,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country,
    string? Phone,
    bool IsDefault);

public sealed record CustomerSummaryDto(
    Guid Id,
    string? Email,
    string? DisplayName,
    string? Phone,
    bool AcceptsMarketing,
    decimal StoreCreditBalance,
    int OrderCount,
    decimal LifetimeSpend,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastOrderAt,
    IReadOnlyList<string>? Channels = null);

public sealed record CustomerDetailDto(
    Guid Id,
    string? Email,
    string? DisplayName,
    string? Phone,
    bool AcceptsMarketing,
    decimal StoreCreditBalance,
    int OrderCount,
    decimal LifetimeSpend,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastOrderAt,
    IReadOnlyList<CustomerAddressDto> Addresses,
    IReadOnlyList<OrderSummaryDto> Orders,
    IReadOnlyList<CustomerChannelIdentityDto>? ChannelIdentities = null);

// ── Channel accounts (marketplace presence foundation) ─────────────────────
public sealed record ChannelAccountDto(
    Guid Id,
    string Channel,
    string DisplayName,
    string? ExternalAccountId,
    string? DefaultFeeProfileId,
    bool IsNativelySynced,
    bool IsActive,
    DateTimeOffset? ConnectedAt,
    string? Notes,
    int ActiveListingCount);

public sealed record UpsertChannelAccountRequest(
    string Channel,
    string DisplayName,
    string? ExternalAccountId,
    string? DefaultFeeProfileId,
    bool IsActive,
    string? Notes);

public sealed record ChannelListingDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    Guid ChannelAccountId,
    string ChannelAccountName,
    string Channel,
    ChannelListingStatus Status,
    string? ExternalListingId,
    string? ExternalUrl,
    decimal? ListedPrice,
    int? Quantity,
    string? ImageHash,
    DateTimeOffset? ListedAt,
    DateTimeOffset? SoldAt,
    DateTimeOffset? LastSyncedAt,
    string? LastError);

/// <summary>Create or update the state of one item on one channel account (the channel-presence panel writes this).</summary>
public sealed record UpsertChannelListingRequest(
    Guid ChannelAccountId,
    ChannelListingStatus Status,
    decimal? ListedPrice,
    int? Quantity,
    string? ExternalListingId,
    string? ExternalUrl,
    string? ImageHash);

// ── Org-controlled reference menus ─────────────────────────────────────────
public sealed record OrgReferenceOverrideDto(
    Guid Id,
    string Kind,
    string Value,
    string? ParentValue,
    string? Label,
    bool IsCustom,
    bool IsDisabled);

public sealed record UpsertReferenceOverrideRequest(
    string Kind,
    string Value,
    string? ParentValue,
    string? Label,
    bool IsCustom,
    bool IsDisabled);
