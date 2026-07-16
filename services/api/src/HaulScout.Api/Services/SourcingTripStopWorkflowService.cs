using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Services;

/// <summary>
/// Everything that happens at a stop: planning it, ordering the route, and running it in the field — navigate,
/// arrive, work the checklist, complete or skip. Stop progress drives the trip's own status, so finishing the last
/// stop completes the route.
/// </summary>
public sealed class SourcingTripStopWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    SourcingTripQueryService queries)
{
    public async Task<AppResult<SourcingTripStopDto>> CreateAsync(Guid tripId, UpsertSourcingTripStopRequest request, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.FindTrip(tripId)
            .Include(item => item.Stops)
            .SingleOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return AppResult<SourcingTripStopDto>.NotFound();
        }

        string? validation = ValidateStop(request);
        if (validation is not null)
        {
            return AppResult<SourcingTripStopDto>.Validation(validation);
        }

        int nextOrder = trip.Stops.Count == 0 ? 1 : trip.Stops.Max(item => item.PlannedOrder) + 1;
        SourcingTripStop stop = new()
        {
            OrganizationId = currentUser.OrganizationId,
            SourcingTripId = trip.Id,
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            PlannedOrder = request.PlannedOrder ?? nextOrder
        };

        Apply(request, stop);
        SourcingStopChecklistCatalog.ApplyTemplate(stop);
        db.SourcingTripStops.Add(stop);
        // Adding the first stop turns a draft into a real plan.
        trip.Status = trip.Status == SourcingRouteStatus.Draft ? SourcingRouteStatus.Planned : trip.Status;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingTripStopDto>.Ok(stop.ToDto());
    }

    public async Task<AppResult<SourcingTripStopDto>> UpdateAsync(Guid tripId, Guid stopId, UpsertSourcingTripStopRequest request, CancellationToken cancellationToken)
    {
        SourcingTripStop? stop = await LoadStopAsync(tripId, stopId, cancellationToken);
        if (stop is null)
        {
            return AppResult<SourcingTripStopDto>.NotFound();
        }

        string? validation = ValidateStop(request);
        if (validation is not null)
        {
            return AppResult<SourcingTripStopDto>.Validation(validation);
        }

        stop.Name = request.Name.Trim();
        stop.Address = request.Address.Trim();
        Apply(request, stop);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingTripStopDto>.Ok(stop.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        SourcingTripStop? stop = await db.SourcingTripStops.SingleOrDefaultAsync(
            item => item.Id == stopId && item.SourcingTripId == tripId && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (stop is null)
        {
            return AppResult.NotFound();
        }

        db.SourcingTripStops.Remove(stop);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult<SourcingRouteDetailDto>> ReorderAsync(Guid tripId, ReorderSourcingTripStopsRequest request, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.LoadRouteAsync(tripId, cancellationToken);
        if (trip is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        HashSet<Guid> requestedIds = request.StopIds.ToHashSet();
        if (requestedIds.Count != trip.Stops.Count || trip.Stops.Any(item => !requestedIds.Contains(item.Id)))
        {
            return AppResult<SourcingRouteDetailDto>.Validation("Reorder request must include every stop exactly once.");
        }

        for (int index = 0; index < request.StopIds.Count; index++)
        {
            SourcingTripStop stop = trip.Stops.Single(item => item.Id == request.StopIds[index]);
            stop.PlannedOrder = index + 1;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Detail(trip);
    }

    public async Task<AppResult<SourcingTripStopDto>> ApplyChecklistTemplateAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        SourcingTripStop? stop = await LoadStopAsync(tripId, stopId, cancellationToken);
        if (stop is null)
        {
            return AppResult<SourcingTripStopDto>.NotFound();
        }

        SourcingStopChecklistCatalog.ApplyTemplate(stop);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingTripStopDto>.Ok(stop.ToDto());
    }

    public async Task<AppResult<SourcingRouteDetailDto>> NavigateAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        (SourcingTrip? trip, SourcingTripStop? stop) = await LoadRouteStopAsync(tripId, stopId, cancellationToken);
        if (trip is null || stop is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        trip.Status = SourcingRouteStatus.Active;
        stop.Status = SourcingStopStatus.Navigating;
        await db.SaveChangesAsync(cancellationToken);
        return Detail(trip);
    }

    public async Task<AppResult<SourcingRouteDetailDto>> ArriveAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        (SourcingTrip? trip, SourcingTripStop? stop) = await LoadRouteStopAsync(tripId, stopId, cancellationToken);
        if (trip is null || stop is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        trip.Status = SourcingRouteStatus.Active;
        stop.ActualArrivalAt ??= DateTimeOffset.UtcNow;
        stop.Status = SourcingStopStatus.InProgress;
        await db.SaveChangesAsync(cancellationToken);
        return Detail(trip);
    }

    public async Task<AppResult<SourcingRouteDetailDto>> CompleteAsync(Guid tripId, Guid stopId, CompleteSourcingStopRequest request, CancellationToken cancellationToken)
    {
        (SourcingTrip? trip, SourcingTripStop? stop) = await LoadRouteStopAsync(tripId, stopId, cancellationToken);
        if (trip is null || stop is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        bool hasIncompleteRequired = stop.ChecklistItems.Any(item => item.IsRequired && !item.IsCompleted);
        if (hasIncompleteRequired && !request.Force)
        {
            return AppResult<SourcingRouteDetailDto>.Validation("Required checklist items are incomplete.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        stop.ActualArrivalAt ??= now;
        stop.ActualDepartureAt = now;
        stop.Status = SourcingStopStatus.Completed;

        // Finishing the last outstanding stop finishes the route.
        if (trip.Stops.All(item => item.Status is SourcingStopStatus.Completed or SourcingStopStatus.Skipped or SourcingStopStatus.Cancelled))
        {
            trip.Status = SourcingRouteStatus.Completed;
            trip.EndedAt ??= now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Detail(trip);
    }

    public async Task<AppResult<SourcingRouteDetailDto>> SkipAsync(Guid tripId, Guid stopId, SkipSourcingStopRequest request, CancellationToken cancellationToken)
    {
        (SourcingTrip? trip, SourcingTripStop? stop) = await LoadRouteStopAsync(tripId, stopId, cancellationToken);
        if (trip is null || stop is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        if (!stop.CanBeSkipped)
        {
            return AppResult<SourcingRouteDetailDto>.Validation("This stop is marked as required.");
        }

        stop.Status = SourcingStopStatus.Skipped;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            stop.Notes = string.Join("\n", new[] { stop.Notes, $"Skip reason: {request.Reason.Trim()}" }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Detail(trip);
    }

    public async Task<AppResult<SourcingStopChecklistItemDto>> CreateChecklistItemAsync(Guid tripId, Guid stopId, CreateChecklistItemRequest request, CancellationToken cancellationToken)
    {
        SourcingTripStop? stop = await db.SourcingTripStops
            .Include(item => item.ChecklistItems)
            .SingleOrDefaultAsync(
                item => item.Id == stopId && item.SourcingTripId == tripId && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
        if (stop is null)
        {
            return AppResult<SourcingStopChecklistItemDto>.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return AppResult<SourcingStopChecklistItemDto>.Validation("Checklist item text is required.");
        }

        SourcingStopChecklistItem item = new()
        {
            OrganizationId = currentUser.OrganizationId,
            SourcingTripStopId = stop.Id,
            Text = request.Text.Trim(),
            SortOrder = stop.ChecklistItems.Count == 0 ? 1 : stop.ChecklistItems.Max(checklistItem => checklistItem.SortOrder) + 1,
            IsRequired = request.IsRequired
        };

        db.SourcingStopChecklistItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingStopChecklistItemDto>.Ok(item.ToDto());
    }

    public async Task<AppResult<SourcingRouteDetailDto>> UpdateChecklistItemAsync(
        Guid tripId,
        Guid stopId,
        Guid checklistItemId,
        UpdateChecklistItemRequest request,
        CancellationToken cancellationToken)
    {
        (SourcingTrip? trip, SourcingTripStop? stop) = await LoadRouteStopAsync(tripId, stopId, cancellationToken);
        SourcingStopChecklistItem? checklistItem = stop?.ChecklistItems.SingleOrDefault(item => item.Id == checklistItemId);
        if (trip is null || stop is null || checklistItem is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        checklistItem.IsCompleted = request.IsCompleted;
        checklistItem.CompletedAt = request.IsCompleted ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(cancellationToken);
        return Detail(trip);
    }

    private static AppResult<SourcingRouteDetailDto> Detail(SourcingTrip trip) =>
        AppResult<SourcingRouteDetailDto>.Ok(SourcingTripQueryService.BuildRouteDetail(trip));

    private Task<SourcingTripStop?> LoadStopAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken) =>
        db.SourcingTripStops
            .Include(item => item.Items)
            .Include(item => item.ChecklistItems)
            .SingleOrDefaultAsync(
                item => item.Id == stopId && item.SourcingTripId == tripId && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

    private async Task<(SourcingTrip? Trip, SourcingTripStop? Stop)> LoadRouteStopAsync(Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.LoadRouteSlimAsync(tripId, cancellationToken);
        return (trip, SourcingTripQueryService.FindStop(trip, stopId));
    }

    private static void Apply(UpsertSourcingTripStopRequest request, SourcingTripStop stop)
    {
        stop.SourceType = request.SourceType;
        stop.Phone = TrimToNull(request.Phone);
        stop.Url = TrimToNull(request.Url);
        stop.Notes = TrimToNull(request.Notes);
        stop.PlannedOrder = request.PlannedOrder ?? stop.PlannedOrder;
        stop.PlannedArrivalAt = request.PlannedArrivalAt;
        stop.EstimatedDurationMinutes = request.EstimatedDurationMinutes;
        stop.Priority = request.Priority;
        stop.IsTimeSensitive = request.IsTimeSensitive;
        stop.TimeWindowStartAt = request.TimeWindowStartAt;
        stop.TimeWindowEndAt = request.TimeWindowEndAt;
        stop.CanBeSkipped = request.CanBeSkipped;
        stop.BudgetCap = request.BudgetCap;
    }

    private static string? ValidateStop(UpsertSourcingTripStopRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Stop name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return "Stop address is required.";
        }

        if (request.EstimatedDurationMinutes <= 0)
        {
            return "Estimated duration must be greater than zero.";
        }

        if (request.BudgetCap is < 0)
        {
            return "Stop budget cannot be negative.";
        }

        if (request.TimeWindowStartAt.HasValue
            && request.TimeWindowEndAt.HasValue
            && request.TimeWindowStartAt.Value > request.TimeWindowEndAt.Value)
        {
            return "Time window start must be before the end.";
        }

        return null;
    }
}
