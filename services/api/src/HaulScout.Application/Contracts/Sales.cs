using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Sales money: recording, corrections, fees, refunds, returns, bundles, grouping, profit.

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
