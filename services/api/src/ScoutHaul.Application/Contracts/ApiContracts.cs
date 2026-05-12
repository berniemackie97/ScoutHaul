using ScoutHaul.Domain;

namespace ScoutHaul.Application.Contracts;

public sealed record CreateOrganizationRequest(string Name);
public sealed record AddOrganizationMemberRequest(string Email, string DisplayName, OrganizationRole Role);
public sealed record OrganizationDto(Guid Id, string Name, DateTimeOffset CreatedAt);
public sealed record OrganizationMemberDto(Guid Id, Guid UserId, string Email, string DisplayName, OrganizationRole Role, DateTimeOffset JoinedAt);

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
    Guid? SourceLocationId,
    string Title,
    string? Description,
    string? Condition,
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
    Guid? SourceLocationId,
    string Title,
    string? Description,
    string? Condition,
    InventoryStatus Status,
    decimal? SellerAskingPrice,
    decimal? PurchasePrice,
    decimal? EstimatedSalePrice,
    string? Barcode,
    string? Notes,
    bool IsArchived,
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
    DateTimeOffset? SoldDate);

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
    string? ProductCategory = null);

public sealed record CreateSourcingTripRequest(string Name, DateTimeOffset? StartedAt, decimal? CashBudget, string? Notes);
public sealed record UpdateSourcingTripRequest(string Name, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, decimal? CashBudget, string? Notes);
public sealed record AddItemToTripRequest(Guid InventoryItemId);
public sealed record SourcingTripDto(Guid Id, string Name, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, decimal? CashBudget, string? Notes, int ItemCount, DateTimeOffset CreatedAt);
public sealed record SourcingTripSummaryDto(Guid Id, string Name, int ItemCount, decimal TotalPurchaseCost, decimal TotalAskingPrice, decimal? CashBudget, decimal RemainingBudget);

public sealed record CreateBudgetRequest(string Name, decimal StartingAmount, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);
public sealed record BudgetDto(Guid Id, string Name, decimal StartingAmount, decimal CurrentAmount, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool IsActive);
public sealed record AddExpenseRequest(Guid? InventoryItemId, Guid? SourcingTripId, ExpenseCategory Category, string Description, decimal Amount, DateTimeOffset? OccurredAt);
public sealed record ExpenseDto(Guid Id, Guid? InventoryItemId, Guid? SourcingTripId, ExpenseCategory Category, string Description, decimal Amount, DateTimeOffset OccurredAt);
public sealed record BudgetSummaryDto(Guid? BudgetId, decimal StartingAmount, decimal CurrentAmount, decimal TotalExpenses, decimal InventoryPurchases, decimal RemainingAmount);

public sealed record CreateListingRequest(Guid InventoryItemId, string Platform, string? ListingUrl, decimal ListPrice, string? Notes);
public sealed record UpdateListingRequest(string Platform, string? ListingUrl, decimal ListPrice, ListingStatus Status, string? Notes);
public sealed record ListingDto(Guid Id, Guid InventoryItemId, string Platform, string? ListingUrl, decimal ListPrice, ListingStatus Status, DateTimeOffset? ListedAt, DateTimeOffset? EndedAt, string? Notes);
public sealed record RecordSaleRequest(Guid InventoryItemId, Guid? ListingId, string Platform, decimal SoldPrice, decimal PlatformFees, decimal PaymentFees, decimal ShippingCost, decimal SuppliesCost, decimal OtherCosts, DateTimeOffset? SoldAt, string? Notes);
public sealed record SaleDto(Guid Id, Guid InventoryItemId, Guid? ListingId, string Platform, decimal SoldPrice, decimal PlatformFees, decimal PaymentFees, decimal ShippingCost, decimal SuppliesCost, decimal OtherCosts, decimal NetProfit, SaleStatus Status, DateTimeOffset SoldAt, string? Notes);
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
