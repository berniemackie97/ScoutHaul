using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using System.Text.Json;

namespace HaulScout.Api.Mapping;

public static class DtoMapping
{
    public static OrganizationDto ToDto(this Organization item) => new(item.Id, item.Name, item.CreatedAt);

    public static OrganizationMemberDto ToDto(this OrganizationMember item) => new(
        item.Id,
        item.UserId,
        item.User?.Email ?? string.Empty,
        item.User?.DisplayName ?? string.Empty,
        item.Role,
        item.RoleId,
        item.RoleDefinition?.Name,
        string.IsNullOrWhiteSpace(item.User?.ExternalSubject) ? "Pending" : "Active",
        item.JoinedAt);

    public static OrganizationRoleDto ToDto(this OrganizationRoleDefinition item) => new(
        item.Id,
        item.Name,
        item.Description,
        item.IsSystem,
        item.IsOwnerRole,
        item.Permissions.Select(permission => permission.Permission).Order().ToArray());

    public static OrganizationTeamDto ToDto(this OrganizationTeam item) => new(
        item.Id,
        item.OrganizationId,
        item.Name,
        item.Description,
        item.Members.Count,
        item.CreatedAt);

    public static OrganizationTeamMemberDto ToDto(this OrganizationTeamMember item) => new(
        item.Id,
        item.TeamId,
        item.UserId,
        item.User?.Email ?? string.Empty,
        item.User?.DisplayName ?? string.Empty,
        item.RoleId,
        item.RoleDefinition?.Name,
        item.JoinedAt);

    public static OrganizationInviteCodeDto ToDto(this OrganizationInviteCode item) => new(
        item.Id,
        item.OrganizationId,
        item.TeamId,
        item.Team?.Name,
        item.RoleId,
        item.RoleDefinition?.Name,
        item.Name,
        item.CodePrefix,
        item.IsActive,
        item.ExpiresAt,
        item.MaxUses,
        item.UseCount,
        item.CreatedAt);

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

    public static InventoryItemDto ToDto(this InventoryItem item) => new(
        item.Id,
        item.ProductIdentityId,
        item.SourcingTripId,
        item.SourcingTripStopId,
            item.SourceLocationId,
            item.Title,
            item.Description,
            item.Category,
            item.Brand,
            item.Model,
            item.Color,
            item.Condition,
            item.SizeSystem,
            item.SizeValue,
            item.AttributesJson,
            item.MeasurementsJson,
            item.StorageLocation,
            item.Accessories,
            item.MissingParts,
            item.TestingNotes,
            item.CleaningNotes,
            item.ListingNotes,
            item.Status,
            item.SellerAskingPrice,
            item.PurchasePrice,
            item.EstimatedSalePrice,
            item.Barcode,
            item.Notes,
            ParsePhotoUrls(item.PhotoLocalReferencesJson),
            item.Photos
                .OrderByDescending(photo => photo.IsHero)
                .ThenBy(photo => photo.SortOrder)
                .ThenBy(photo => photo.CreatedAt)
                .Select(photo => photo.ToDto())
                .ToArray(),
            item.Photos
                .OrderByDescending(photo => photo.IsHero)
                .ThenBy(photo => photo.SortOrder)
                .ThenBy(photo => photo.CreatedAt)
                .Select(photo => $"/api/inventory/{item.Id}/photos/{photo.Id}/thumbnail")
                .FirstOrDefault(),
            item.IsArchived,
            item.IsBundle,
            item.BundleComponents.Count,
            item.BundleComponents
                .OrderBy(component => component.ComponentInventoryItem != null ? component.ComponentInventoryItem.Title : component.TitleSnapshot)
                .Select(component => component.ToDto())
                .ToArray(),
            item.AcquisitionLotItems
                .OrderByDescending(lotItem => lotItem.UpdatedAt)
                .Select(lotItem => (Guid?)lotItem.AcquisitionLotId)
                .FirstOrDefault(),
            item.AcquisitionLotItems
                .OrderByDescending(lotItem => lotItem.UpdatedAt)
                .Select(lotItem => lotItem.AcquisitionLot != null ? lotItem.AcquisitionLot.Name : null)
                .FirstOrDefault(),
            item.AcquisitionLotItems
                .OrderByDescending(lotItem => lotItem.UpdatedAt)
                .Select(lotItem => (decimal?)lotItem.AllocatedCost)
                .FirstOrDefault(),
            InventoryLifecycle.Group(item.Status),
            InventoryLifecycle.IsSold(item.Status),
            item.CreatedAt,
            item.UpdatedAt,
            item.OwnershipType,
            item.FunctionalStatus,
            item.CompletenessStatus,
            item.AuthenticityStatus,
            item.PackagingStatus,
            item.ManufacturerPartNumber,
            item.SerialNumber,
            item.CountryOfOrigin,
            item.ManufactureYear,
            item.Length,
            item.Width,
            item.Height,
            item.DimensionUnit,
            item.Weight,
            item.WeightUnit);

    public static AcquisitionLotDto ToDto(this AcquisitionLot item) => new(
        item.Id,
        item.Name,
        item.SupplierName,
        item.SourceLocationId,
        item.SourcingTripId,
        item.SourcingTripStopId,
        item.PurchasedAt,
        item.PurchaseSubtotal,
        item.ShippingCost,
        item.TaxCost,
        item.FeeCost,
        item.OtherCost,
        item.TotalLandedCost,
        item.Items.Sum(lotItem => lotItem.AllocatedCost),
        item.AllocationMethod,
        item.ExternalReference,
        item.Notes,
        item.Items
            .OrderBy(lotItem => lotItem.InventoryItem!.Title)
            .Select(lotItem => lotItem.ToDto())
            .ToArray(),
        item.CreatedAt,
        item.UpdatedAt);

    public static AcquisitionLotItemDto ToDto(this AcquisitionLotItem item) => new(
        item.Id,
        item.InventoryItemId,
        item.InventoryItem?.Title ?? string.Empty,
        item.InventoryItem?.Status ?? InventoryStatus.Draft,
        item.AllocatedCost,
        item.AllocationWeight,
        item.Notes);

    public static InventoryBundleComponentDto ToDto(this InventoryBundleComponent item) => new(
        item.Id,
        item.BundleInventoryItemId,
        item.ComponentInventoryItemId,
        item.ComponentInventoryItem?.Title ?? item.TitleSnapshot,
        item.ComponentInventoryItem?.Status ?? InventoryStatus.Draft,
        item.Quantity,
        item.CostSnapshot,
        item.Notes);

    public static InventoryPhotoDto ToDto(this InventoryPhoto item) => new(
        item.Id,
        item.InventoryItemId,
        item.IsHero,
        item.SortOrder,
        $"/api/inventory/{item.InventoryItemId}/photos/{item.Id}/original",
        $"/api/inventory/{item.InventoryItemId}/photos/{item.Id}/thumbnail",
        item.ContentType,
        item.OriginalByteSize,
        item.ThumbnailByteSize,
        item.Width,
        item.Height,
        item.CreatedAt);

    public static InventoryIssueDto ToDto(this InventoryIssue item) => new(
        item.Id,
        item.InventoryItemId,
        item.InventoryPhotoId,
        item.IssueType,
        item.Severity,
        item.Note,
        item.SortOrder,
        item.IsResolved,
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
        item.Notes);

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

    /// <summary>The payout a marketplace should deposit: item + buyer shipping − fees, plus any tax the seller
    /// keeps (when the platform does not remit it). Compared to the imported payout to surface fee/import drift.</summary>
    public static decimal ExpectedPayout(this PlatformOrder order) =>
        order.ItemsSubtotal + order.ShippingCharged - order.FeeTotal - (order.SellerShippingCost ?? 0) + (order.TaxRemittedByPlatform ? 0 : order.TaxCollected);

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

    public static PlatformOrderDto ToDto(this PlatformOrder order, IReadOnlyDictionary<Guid, (Guid Id, string Title, bool AlreadySold)>? lineSuggestions = null)
    {
        decimal expectedPayout = order.ExpectedPayout();
        return new PlatformOrderDto(
            order.Id,
            order.Platform,
            order.ExternalOrderId,
            order.BuyerName,
            order.BuyerReference,
            order.BuyerEmail,
            order.BuyerNote,
            order.ShipToName,
            order.ShipToPhone,
            order.ShipToLine1,
            order.ShipToLine2,
            order.ShipToCity,
            order.ShipToRegion,
            order.ShipToPostalCode,
            order.ShipToCountry,
            order.OrderedAt,
            order.ItemsSubtotal,
            order.ShippingCharged,
            order.TaxCollected,
            order.FeeTotal,
            order.SellerShippingCost,
            order.PayoutAmount,
            expectedPayout,
            order.PayoutAmount.HasValue ? order.PayoutAmount.Value - expectedPayout : 0,
            order.Currency,
            order.TaxRemittedByPlatform,
            order.FeeBreakdownJson,
            order.Carrier,
            order.TrackingNumber,
            order.PaidAt,
            order.ShippedAt,
            order.Status,
            order.ReconciledSaleGroupId,
            order.ReconciledAt,
            order.OrderedAt.ToUniversalTime(),
            order.ScrapedAt,
            order.Lines
                .OrderBy(line => line.CreatedAt)
                .Select(line =>
                {
                    (Guid Id, string Title, bool AlreadySold)? suggestion = lineSuggestions is not null && lineSuggestions.TryGetValue(line.Id, out (Guid Id, string Title, bool AlreadySold) match) ? match : null;
                    return new PlatformOrderLineDto(
                        line.Id,
                        line.Title,
                        line.UnitPrice,
                        line.Quantity,
                        line.ExternalItemId,
                        line.Sku,
                        line.ShippingCharged,
                        line.TaxCollected,
                        line.PlatformFees,
                        line.SellerShippingCost,
                        line.NetSales,
                        line.TotalSales,
                        line.FeeBreakdownJson,
                        line.InventoryItemId,
                        line.InventoryItem?.Title,
                        suggestion?.Id,
                        suggestion?.Title,
                        suggestion?.AlreadySold ?? false);
                })
                .ToList());
    }

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

    public static OrderSummaryDto ToSummaryDto(this Order item) => new(
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
        item.BuyerReference);

    public static CustomerChannelIdentityDto ToDto(this CustomerChannelIdentity item) => new(
        item.Id,
        item.Channel,
        item.ExternalUserId,
        item.DisplayName,
        item.Email,
        item.FirstSeenAt,
        item.LastSeenAt);

    public static OrderDetailDto ToDetailDto(this Order item, IReadOnlyDictionary<Guid, Sale>? salesByInventoryItem = null)
    {
        Sale? SaleFor(OrderLine line) =>
            salesByInventoryItem is not null && salesByInventoryItem.TryGetValue(line.InventoryItemId, out Sale? sale)
                ? sale
                : null;

        decimal cogs = item.Lines.Sum(line => (line.UnitCostSnapshot ?? 0) * line.Quantity);
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
            item.ShippedAt,
            item.DeliveredAt,
            item.CancelledAt,
            item.Lines.OrderBy(line => line.TitleSnapshot).Select(line => line.ToDto(SaleFor(line))).ToList(),
            item.Channel,
            item.ExternalOrderId,
            item.ExternalOrderUrl,
            item.BuyerReference,
            totalRefunded);
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

    private static IReadOnlyList<string> ParsePhotoUrls(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
