using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
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
            item.CreatedAt,
            item.UpdatedAt);

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
        item.Notes);

    public static SaleCorrectionDto ToDto(this SaleCorrection item) => new(
        item.Id,
        item.SaleId,
        item.Version,
        item.Reason,
        item.CorrectedByUser?.DisplayName ?? item.CorrectedByUser?.Email ?? "Operator",
        item.CreatedAt,
        item.BeforeJson,
        item.AfterJson);

    public static ResaleTaskDto ToDto(this ResaleTask item) => new(
        item.Id,
        item.InventoryItemId,
        item.AssignedToUserId,
        item.Title,
        item.Description,
        item.Status,
        item.DueAt,
        item.CreatedAt);

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
