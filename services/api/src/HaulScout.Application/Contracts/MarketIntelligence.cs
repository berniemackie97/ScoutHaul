using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// What an item is worth: observations, deal analysis, comps, barcode + marketplace lookups.

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

/// <summary>A sold or active listing a grounded web search reported. Read by a model, so unverified.</summary>
public sealed record ResaleComp(
    decimal Price,
    string Status,      // "sold" | "active"
    string? Condition,
    string? Note,
    string? SourceUrl = null,
    string? Title = null,
    DateTimeOffset? Date = null);

/// <summary>A web source the grounded search used (for "verify"/"open listing" links).</summary>
public sealed record ResaleSource(string Url, string? Title);
