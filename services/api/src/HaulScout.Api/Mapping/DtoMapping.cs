using System.Text.Json;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Services;

namespace HaulScout.Api.Mapping;

public static class DtoMapping
{
    public static ProductIdentityDto ToDto(this ProductIdentity item) => new(
        item.Id,
        item.Title,
        item.Brand,
        item.Model,
        item.Category,
        item.Upc,
        item.Ean,
        item.Isbn,
        item.Sku,
        item.SearchQuery,
        item.Notes,
        item.CreatedAt,
        item.UpdatedAt);

    public static MarketObservationDto ToDto(this MarketObservation item) => new(
        item.Id,
        item.ProductIdentityId,
        item.InventoryItemId,
        item.ObservationType,
        item.Source,
        item.SourceUrl,
        item.Title,
        item.Price,
        item.ShippingPrice,
        item.TotalPrice,
        item.Currency,
        item.Condition,
        item.ObservedAt,
        item.SoldAt,
        item.Notes);

    public static SourcingTripDto ToDto(this SourcingTrip item) => new(
        item.Id,
        item.AssignedToUserId,
        item.Name,
        item.StartedAt,
        item.EndedAt,
        item.RouteDate,
        item.PlannedStartAt,
        item.PlannedEndAt,
        item.RouteType,
        item.Status,
        item.CashBudget,
        item.Notes,
        item.Items.Count,
        item.Stops.Count,
        item.CreatedAt);

    public static SourcingTripStopDto ToDto(this SourcingTripStop item) => new(
        item.Id,
        item.SourcingTripId,
        item.Name,
        item.SourceType,
        item.Address,
        item.Phone,
        item.Url,
        item.Notes,
        item.PlannedOrder,
        item.PlannedArrivalAt,
        item.EstimatedDurationMinutes,
        item.Priority,
        item.IsTimeSensitive,
        item.TimeWindowStartAt,
        item.TimeWindowEndAt,
        item.CanBeSkipped,
        item.BudgetCap,
        item.Status,
        item.ActualArrivalAt,
        item.ActualDepartureAt,
        item.Items.Sum(inventoryItem => inventoryItem.PurchasePrice ?? 0),
        item.Items.Count(inventoryItem => inventoryItem.PurchasePrice.HasValue),
        item.ChecklistItems.OrderBy(checklistItem => checklistItem.SortOrder).Select(checklistItem => checklistItem.ToDto()).ToList(),
        item.CreatedAt);

    public static SourcingStopChecklistItemDto ToDto(this SourcingStopChecklistItem item) => new(
        item.Id,
        item.SourcingTripStopId,
        item.Text,
        item.SortOrder,
        item.IsRequired,
        item.IsCompleted,
        item.CompletedAt);

    public static BudgetDto ToDto(this Budget item) => new(
        item.Id,
        item.Name,
        item.StartingAmount,
        item.CurrentAmount,
        item.StartsAt,
        item.EndsAt,
        item.IsActive);

    public static ExpenseDto ToDto(this Expense item) => new(
        item.Id,
        item.InventoryItemId,
        item.SourcingTripId,
        item.SourcingTripStopId,
        item.Category,
        item.Description,
        item.Amount,
        item.OccurredAt);

    public static ListingDto ToDto(this Listing item) => new(
        item.Id,
        item.InventoryItemId,
        item.InventoryItem?.Title ?? string.Empty,
        item.InventoryItem?.Status ?? default,
        item.Platform,
        item.ListingUrl,
        item.ListPrice,
        item.Status,
        item.ListedAt,
        item.EndedAt,
        item.Notes,
        item.Title,
        item.Description);

    public static SaleDto ToDto(this Sale item) => new(
        item.Id,
        item.InventoryItemId,
        item.InventoryItem?.Title ?? string.Empty,
        item.ListingId,
        item.Platform,
        item.SoldPrice,
        item.ShippingCharged,
        item.CostOfGoods,
        item.PlatformFees,
        item.PaymentFees,
        item.ShippingCost,
        item.SuppliesCost,
        item.OtherCosts,
        item.SalesTaxCollected,
        item.TaxRemittedByPlatform,
        ResaleFinancialCalculator.SalesTaxOwed(item.SalesTaxCollected, item.TaxRemittedByPlatform),
        item.NetProfit,
        item.Status,
        item.SoldAt,
        item.Notes,
        SaleWorkflow.Next(item.Status),
        // Prefer the real Order; fall back to the legacy group columns for rows written before the unification.
        item.OrderId ?? item.SaleGroupId,
        item.Order?.ExternalOrderId ?? item.Order?.OrderNumber ?? item.GroupOrderReference,
        item.Refunds.Sum(refund => refund.ItemAmount + refund.ShippingRefunded),
        item.Refunds.Sum(refund => refund.FeeCredit),
        item.Refunds.Count,
        item.OrderId);

    public static RefundDto ToDto(this Refund item) => new(
        item.Id,
        item.SaleId,
        item.Type,
        item.ItemAmount,
        item.ShippingRefunded,
        item.TaxRefunded,
        item.FeeCredit,
        item.ItemReturned,
        item.Reason,
        item.Notes,
        item.RefundedAt,
        item.RecordedByUser?.DisplayName ?? item.RecordedByUser?.Email ?? "Operator");

    public static ReturnDto ToDto(this Return item) => new(
        item.Id,
        item.SaleId,
        item.OrderId,
        item.Sale?.InventoryItem?.Title ?? string.Empty,
        item.Status,
        item.ReturnShippingCost,
        item.Reason,
        item.Notes,
        item.Restocked,
        item.RefundId,
        item.RequestedAt,
        item.LabelIssuedAt,
        item.ReceivedAt,
        item.ResolvedAt);

    public static SaleShipmentDto ToDto(this Shipment item) => new(
        item.Id,
        item.SaleId,
        item.Carrier,
        item.TrackingNumber,
        item.Cost,
        item.PackedAt,
        item.ShippedAt,
        item.DeliveredAt);

    public static SaleCorrectionDto ToDto(this SaleCorrection item) => new(
        item.Id,
        item.SaleId,
        item.Version,
        item.Reason,
        item.CorrectedByUser?.DisplayName ?? item.CorrectedByUser?.Email ?? "Operator",
        item.CreatedAt,
        item.BeforeJson,
        item.AfterJson);

    public static PlatformListingDto ToDto(this PlatformListing listing, (Guid Id, string Title, bool AlreadySold)? suggestion = null) => new(
        listing.Id,
        listing.Platform,
        listing.ExternalListingId,
        listing.Title,
        listing.Price,
        listing.Quantity,
        listing.QuantitySold,
        listing.Currency,
        listing.Status,
        listing.Format,
        listing.Sku,
        listing.CategoryName,
        listing.Condition,
        listing.Url,
        listing.PrimaryImageUrl,
        listing.ListedAt,
        listing.EndsAt,
        listing.ReconcileStatus,
        listing.ReconciledInventoryItemId,
        listing.ReconciledInventoryItem?.Title,
        suggestion?.Id,
        suggestion?.Title,
        suggestion?.AlreadySold ?? false,
        listing.ScrapedAt);

    public static PlatformListingQualitySnapshotDto ToDto(this PlatformListingQualitySnapshot snapshot) => new(
        snapshot.Id,
        snapshot.Platform,
        snapshot.ExternalListingId,
        snapshot.Title,
        snapshot.CategoryName,
        snapshot.ListingCondition,
        snapshot.PriceRange,
        snapshot.ReportGeneratedAt,
        snapshot.ImportedAt,
        snapshot.PlatformListingId,
        snapshot.Action,
        snapshot.DailyImpressions,
        snapshot.ClickThroughRate,
        snapshot.SalesConversionRate,
        snapshot.PromotedListingsAdRate,
        snapshot.RecommendedItemSpecificsFilled,
        snapshot.RecommendedItemSpecificsToAdd,
        snapshot.Brand,
        snapshot.Mpn,
        snapshot.Upc,
        snapshot.PhotoCount,
        snapshot.TitleKeywordCount,
        snapshot.FreeShipping,
        snapshot.EstimatedHandlingDays,
        snapshot.AcceptsReturns,
        snapshot.SellerPaidReturns,
        snapshot.PromotedListings,
        snapshot.BestOffer);

    public static ResaleTaskDto ToDto(this ResaleTask item) => new(
        item.Id,
        item.InventoryItemId,
        item.AssignedToUserId,
        item.Title,
        item.Description,
        item.Status,
        item.DueAt,
        item.CreatedAt);

    public static OrderLineDto ToDto(this OrderLine item, Sale? sale = null) => new(
        item.Id,
        item.InventoryItemId,
        item.TitleSnapshot,
        item.SkuSnapshot,
        item.UnitPrice,
        item.Quantity,
        item.LineTotal,
        item.UnitCostSnapshot,
        sale?.ToDto());

    /// <param name="hasOpenReturn">
    /// Whether a return is in flight. The caller supplies it: it lives on Return, not on the order, and
    /// the list resolves it once for the whole page rather than per row.
    /// </param>
    public static OrderSummaryDto ToSummaryDto(this Order item, bool hasOpenReturn = false) => new(
        item.Id,
        item.OrderNumber,
        item.Status,
        item.PaymentStatus,
        item.FulfillmentStatus,
        item.Email,
        item.Customer?.DisplayName,
        item.CustomerId,
        item.GrandTotal,
        item.Currency,
        item.Lines.Sum(line => line.Quantity),
        item.IsLocalPickup,
        item.CreatedAt,
        item.PaidAt,
        item.ShippingCarrier,
        item.TrackingNumber,
        item.ShippedAt,
        item.DeliveredAt,
        item.Channel,
        item.ExternalOrderId,
        item.BuyerReference,
        OrderWorkflow.StageFor(item.ToWorkflowFacts(hasOpenReturn)).ToString(),
        item.Lines.OrderBy(line => line.TitleSnapshot).Select(line => line.TitleSnapshot).FirstOrDefault(),
        item.TrackingUrl);

    public static CustomerChannelIdentityDto ToDto(this CustomerChannelIdentity item) => new(
        item.Id,
        item.Channel,
        item.ExternalUserId,
        item.DisplayName,
        item.Email,
        item.FirstSeenAt,
        item.LastSeenAt);

    public static OrderDetailDto ToDetailDto(
        this Order item,
        IReadOnlyDictionary<Guid, Sale>? salesByInventoryItem = null,
        bool hasOpenReturn = false)
    {
        Sale? SaleFor(OrderLine line) =>
            salesByInventoryItem is not null && salesByInventoryItem.TryGetValue(line.InventoryItemId, out Sale? sale)
                ? sale
                : null;

        // The sale's COGS is the live figure; the line snapshot goes stale when a cost is entered after the sale.
        decimal cogs = item.Lines.Sum(line => SaleFor(line)?.CostOfGoods ?? (line.UnitCostSnapshot ?? 0) * line.Quantity);
        // Refunds live on the sale lines; roll them up so the order shows what the buyer was actually charged net.
        decimal totalRefunded = salesByInventoryItem is null
            ? 0
            : salesByInventoryItem.Values.Sum(sale => sale.Refunds.Sum(refund => refund.ItemAmount + refund.ShippingRefunded));
        return new(
            item.Id,
            item.OrderNumber,
            item.Status,
            item.PaymentStatus,
            item.FulfillmentStatus,
            item.Email,
            item.Customer?.DisplayName,
            item.CustomerId,
            item.Subtotal,
            item.DiscountTotal,
            item.TaxTotal,
            item.ShippingTotal,
            item.GrandTotal,
            item.Currency,
            cogs,
            item.Subtotal - item.DiscountTotal - cogs,
            item.ShipToName,
            item.ShipToLine1,
            item.ShipToLine2,
            item.ShipToCity,
            item.ShipToRegion,
            item.ShipToPostalCode,
            item.ShipToCountry,
            item.IsLocalPickup,
            item.CustomerNote,
            item.ShippingCarrier,
            item.TrackingNumber,
            item.TrackingUrl,
            item.StripePaymentIntentId,
            item.CreatedAt,
            item.PaidAt,
            item.PackedAt,
            item.ShippedAt,
            item.DeliveredAt,
            item.CancelledAt,
            item.Lines.OrderBy(line => line.TitleSnapshot).Select(line => line.ToDto(SaleFor(line))).ToList(),
            item.Channel,
            item.ExternalOrderId,
            item.ExternalOrderUrl,
            item.BuyerReference,
            totalRefunded,
            item.ToWorkflowState(hasOpenReturn));
    }

    /// <summary>
    /// The order's stage and the actions it allows, answered by the domain rather than re-derived per
    /// surface. This is what stops one client offering "mark delivered" on an unpaid, unshipped order
    /// while another refuses it.
    /// </summary>
    /// <summary>The order's own facts, for a caller that only needs to test a transition.</summary>
    public static OrderWorkflowFacts ToWorkflowFacts(this Order item, bool hasOpenReturn = false) => new(
        item.Status,
        item.PaymentStatus,
        item.IsLocalPickup,
        IsPacked: item.PackedAt is not null,
        HasCarrier: !string.IsNullOrWhiteSpace(item.ShippingCarrier),
        HasTrackingNumber: !string.IsNullOrWhiteSpace(item.TrackingNumber),
        HasShippingAddress: !string.IsNullOrWhiteSpace(item.ShipToLine1),
        IsShipped: item.ShippedAt is not null,
        IsDelivered: item.DeliveredAt is not null,
        hasOpenReturn,
        Order.ChannelHandlesShipping(item.Channel));

    public static OrderWorkflowStateDto ToWorkflowState(this Order item, bool hasOpenReturn)
    {
        OrderWorkflowFacts facts = item.ToWorkflowFacts(hasOpenReturn);

        IReadOnlyList<OrderWorkflowAction> available = OrderWorkflow.AvailableActions(facts);

        // Explain only what the operator might reasonably reach for next; listing every refusal for
        // every action is noise.
        List<string> blockers = [];
        foreach (OrderWorkflowAction action in new[]
                 {
                     OrderWorkflowAction.MarkPacked,
                     OrderWorkflowAction.Ship,
                     OrderWorkflowAction.MarkPickedUp,
                     OrderWorkflowAction.MarkDelivered,
                 })
        {
            if (available.Contains(action))
            {
                continue;
            }

            OrderTransition transition = OrderWorkflow.Evaluate(facts, action);
            blockers.AddRange(transition.Blockers);
        }

        return new OrderWorkflowStateDto(
            OrderWorkflow.StageFor(facts).ToString(),
            available.Select(action => action.ToString()).ToArray(),
            blockers.Distinct().ToArray());
    }

    public static CustomerAddressDto ToDto(this CustomerAddress item) => new(
        item.Id,
        item.Label,
        item.RecipientName,
        item.Line1,
        item.Line2,
        item.City,
        item.Region,
        item.PostalCode,
        item.Country,
        item.Phone,
        item.IsDefault);

    // Lifetime spend counts only captured (paid) orders; a pending/cancelled order is not revenue yet.
    private static decimal LifetimeSpend(Customer item) =>
        item.Orders.Where(order => order.PaymentStatus == PaymentStatus.Paid).Sum(order => order.GrandTotal);

    private static DateTimeOffset? LastOrderAt(Customer item) =>
        item.Orders.Count > 0 ? item.Orders.Max(order => order.CreatedAt) : null;

    public static CustomerSummaryDto ToSummaryDto(this Customer item) => new(
        item.Id,
        item.Email,
        item.DisplayName,
        item.Phone,
        item.AcceptsMarketing,
        item.StoreCreditBalance,
        item.Orders.Count,
        LifetimeSpend(item),
        item.CreatedAt,
        LastOrderAt(item),
        item.ChannelIdentities.Select(identity => identity.Channel).Distinct().Order().ToList());

    public static CustomerDetailDto ToDetailDto(this Customer item) => new(
        item.Id,
        item.Email,
        item.DisplayName,
        item.Phone,
        item.AcceptsMarketing,
        item.StoreCreditBalance,
        item.Orders.Count,
        LifetimeSpend(item),
        item.CreatedAt,
        LastOrderAt(item),
        item.Addresses.OrderByDescending(address => address.IsDefault).Select(address => address.ToDto()).ToList(),
        item.Orders.OrderByDescending(order => order.CreatedAt).Select(order => order.ToSummaryDto()).ToList(),
        item.ChannelIdentities.OrderBy(identity => identity.Channel).Select(identity => identity.ToDto()).ToList());

    public static ChannelListingDto ToDto(this ChannelListing item) => new(
        item.Id,
        item.InventoryItemId,
        item.InventoryItem?.Title ?? string.Empty,
        item.ChannelAccountId,
        item.ChannelAccount?.DisplayName ?? string.Empty,
        item.ChannelAccount?.Channel ?? string.Empty,
        item.Status,
        item.ExternalListingId,
        item.ExternalUrl,
        item.ListedPrice,
        item.Quantity,
        item.ImageHash,
        item.ListedAt,
        item.SoldAt,
        item.LastSyncedAt,
        item.LastError);

}
