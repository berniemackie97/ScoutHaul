using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Marketplace exports coming in: platform orders/listings/refunds, and reconciling them.

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

public sealed record PlatformOrderDeletedDto(Guid PlatformOrderId);

/// <summary>What settling the backlog would do. Shown before anything is written.</summary>
/// <param name="PendingOrders">Orders currently waiting on a decision.</param>
/// <param name="CanSettle">How many of those need no decision at all.</param>
public sealed record AutoMatchPreviewDto(int PendingOrders, int CanSettle, IReadOnlyList<AutoMatchPreviewOrderDto> Orders);

public sealed record AutoMatchPreviewOrderDto(Guid PlatformOrderId, string ExternalOrderId, string Platform, int LineCount);

public sealed record AutoMatchAppliedDto(int Settled);

/// <param name="AutoReconciled">Orders settled during the import because every line matched exactly — work the operator never has to see.</param>
/// <param name="Skipped">Orders the operator marked Not my inventory, left alone.</param>
/// <param name="Unchanged">Orders re-imported with identical facts: nothing about them changed.</param>
public sealed record IngestPlatformOrdersResultDto(
    int Created,
    int Updated,
    int Skipped,
    int AutoReconciled,
    IReadOnlyList<PlatformOrderDto> Orders,
    int Unchanged = 0);

/// <summary>Import a marketplace's own CSV export (e.g. eBay Seller Hub → Performance → Sales report) — the
/// authoritative fee source. Parsed server-side into the same idempotent ingest path as manual JSON.</summary>
public sealed record ImportPlatformOrdersCsvRequest(string Platform, string Csv);

/// <summary>Import a marketplace's own listings export (eBay Active Listings today) — parsed server-side into the
/// same idempotent listing ingest, so legacy listings seed/link inventory without re-entry.</summary>
public sealed record ImportPlatformListingsCsvRequest(string Platform, string Csv);

/// <summary>Import eBay's Listing Quality workbook (.xlsx). The workbook is binary, so admin sends it as base64.</summary>
public sealed record ImportPlatformListingQualityWorkbookRequest(string Platform, string FileName, string ContentBase64);

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

/// <param name="Unchanged">Listings re-imported with identical facts: nothing about them changed.</param>
public sealed record IngestPlatformListingsResultDto(
    int Created,
    int Updated,
    int Skipped,
    IReadOnlyList<PlatformListingDto> Listings,
    int Unchanged = 0);

public sealed record PlatformListingQualitySnapshotDto(
    Guid Id,
    string Platform,
    string ExternalListingId,
    string Title,
    string? CategoryName,
    string? ListingCondition,
    string? PriceRange,
    DateTimeOffset ReportGeneratedAt,
    DateTimeOffset ImportedAt,
    Guid? PlatformListingId,
    string? Action,
    decimal? DailyImpressions,
    decimal? ClickThroughRate,
    decimal? SalesConversionRate,
    decimal? PromotedListingsAdRate,
    int? RecommendedItemSpecificsFilled,
    string? RecommendedItemSpecificsToAdd,
    string? Brand,
    string? Mpn,
    string? Upc,
    int? PhotoCount,
    int? TitleKeywordCount,
    bool? FreeShipping,
    int? EstimatedHandlingDays,
    bool? AcceptsReturns,
    bool? SellerPaidReturns,
    bool? PromotedListings,
    bool? BestOffer);

public sealed record ImportPlatformListingQualityResultDto(
    int Imported,
    int Updated,
    int MatchedListings,
    int UnmatchedListings,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PlatformListingQualitySnapshotDto> Snapshots);

/// <summary>Turn an imported listing into ScoutHaul inventory: link it to an existing item, or create a new draft
/// item seeded from the listing. Either way a <c>ChannelListing</c> is created/updated so the item shows on eBay.</summary>
public sealed record ReconcilePlatformListingRequest(Guid? InventoryItemId = null, bool CreateNewItem = false);

public sealed record PlatformListingReconcileResultDto(
    Guid PlatformListingId,
    Guid InventoryItemId,
    Guid ChannelListingId,
    bool CreatedInventoryItem,
    PlatformListingDto Listing);
