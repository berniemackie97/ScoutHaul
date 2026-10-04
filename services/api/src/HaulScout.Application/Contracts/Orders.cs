using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Orders on every channel — the operational surface.

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
    string? BuyerReference = null,
    // The queue this order belongs in, so the list can be grouped by work rather than by raw status.
    string? Stage = null,
    // What the operator recognises an order by — the first line's title.
    string? LeadItemTitle = null,
    string? TrackingUrl = null);

/// <summary>
/// Where an order sits in the day's work, and what may be done with it. Derived from the order's own
/// facts by OrderWorkflow, so the admin, the mobile app and any future surface offer the same actions
/// and give the same reason when one is unavailable.
/// </summary>
/// <param name="Blockers">Why the unavailable actions are unavailable, in operator language.</param>
public sealed record OrderWorkflowStateDto(
    string Stage,
    IReadOnlyList<string> AvailableActions,
    IReadOnlyList<string> Blockers);

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
    DateTimeOffset? PackedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<OrderLineDto> Lines,
    string Channel = "Storefront",
    string? ExternalOrderId = null,
    string? ExternalOrderUrl = null,
    string? BuyerReference = null,
    // Buyer money refunded across every line of the order (item + shipping). Net received = GrandTotal − TotalRefunded.
    decimal TotalRefunded = 0,
    OrderWorkflowStateDto? Workflow = null);

/// <param name="Stage">The workflow stage, as <c>OrderWorkflowStage</c> names it.</param>
public sealed record OrderQueueCountDto(string Stage, int Count);

/// <summary>
/// The order book at a glance: how much work is waiting in each stage, and the money behind it.
///
/// These are counts over every order, not over a page of them. The admin portal used to fetch the
/// newest 200 orders, bucket them in the browser, and label the result "Captured revenue · Paid
/// orders" — a figure that stopped being true on the two-hundred-and-first order and never said so.
/// </summary>
/// <param name="CapturedRevenue">Grand total of every paid order.</param>
/// <param name="OpenWork">Orders in a stage that is waiting on someone here — to pack, to ship, ready for pickup.</param>
public sealed record OrderQueueSummaryDto(
    IReadOnlyList<OrderQueueCountDto> Stages,
    int TotalOrders,
    int OpenWork,
    int NeedsAttention,
    decimal CapturedRevenue);

/// <summary>A filtered, paged page of orders plus the queue summary over the whole book.</summary>
public sealed record OrderListDto(
    IReadOnlyList<OrderSummaryDto> Items,
    int Total,
    int Page,
    int PageSize,
    int PageCount,
    OrderQueueSummaryDto Summary);

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
    bool MarkDelivered,
    bool MarkPacked = false);
