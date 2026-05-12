using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain.Entities;

namespace ScoutHaul.Api.Mapping;

public static class DtoMapping
{
    public static OrganizationDto ToDto(this Organization item) => new(item.Id, item.Name, item.CreatedAt);

    public static OrganizationMemberDto ToDto(this OrganizationMember item) => new(
        item.Id,
        item.UserId,
        item.User?.Email ?? string.Empty,
        item.User?.DisplayName ?? string.Empty,
        item.Role,
        item.JoinedAt);

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
        item.SourceLocationId,
        item.Title,
        item.Description,
        item.Condition,
        item.Status,
        item.SellerAskingPrice,
        item.PurchasePrice,
        item.EstimatedSalePrice,
        item.Barcode,
        item.Notes,
        item.IsArchived,
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
        item.Name,
        item.StartedAt,
        item.EndedAt,
        item.CashBudget,
        item.Notes,
        item.Items.Count,
        item.CreatedAt);

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
        item.Category,
        item.Description,
        item.Amount,
        item.OccurredAt);

    public static ListingDto ToDto(this Listing item) => new(
        item.Id,
        item.InventoryItemId,
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
        item.ListingId,
        item.Platform,
        item.SoldPrice,
        item.PlatformFees,
        item.PaymentFees,
        item.ShippingCost,
        item.SuppliesCost,
        item.OtherCosts,
        item.NetProfit,
        item.Status,
        item.SoldAt,
        item.Notes);

    public static ResaleTaskDto ToDto(this ResaleTask item) => new(
        item.Id,
        item.InventoryItemId,
        item.AssignedToUserId,
        item.Title,
        item.Description,
        item.Status,
        item.DueAt,
        item.CreatedAt);
}
