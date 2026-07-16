using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Sourcing routes: trips, stops, checklists.

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
