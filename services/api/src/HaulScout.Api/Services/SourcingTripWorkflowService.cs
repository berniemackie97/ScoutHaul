using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Services;

/// <summary>
/// A sourcing trip's own lifecycle: planning it, running it (start/pause/complete), attaching what was bought, and
/// deleting it. Stop-level actions live in <see cref="SourcingTripStopWorkflowService"/>.
/// </summary>
public sealed class SourcingTripWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    SourcingTripQueryService queries)
{
    public async Task<AppResult<SourcingTripDto>> CreateAsync(CreateSourcingTripRequest request, CancellationToken cancellationToken)
    {
        string? validation = ValidateTrip(request.Name, request.CashBudget);
        if (validation is not null)
        {
            return AppResult<SourcingTripDto>.Validation(validation);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset startedAt = request.StartedAt ?? request.PlannedStartAt ?? now;

        SourcingTrip trip = new()
        {
            OrganizationId = currentUser.OrganizationId,
            AssignedToUserId = request.AssignedToUserId,
            Name = request.Name.Trim(),
            StartedAt = startedAt,
            RouteDate = request.RouteDate ?? DateOnly.FromDateTime(startedAt.Date),
            PlannedStartAt = request.PlannedStartAt ?? startedAt,
            PlannedEndAt = request.PlannedEndAt,
            RouteType = request.RouteType ?? SourcingRouteType.General,
            Status = SourcingRouteStatus.Draft,
            CashBudget = request.CashBudget,
            Notes = TrimToNull(request.Notes)
        };

        db.SourcingTrips.Add(trip);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingTripDto>.Ok(trip.ToDto());
    }

    public async Task<AppResult<SourcingTripDto>> UpdateAsync(Guid id, UpdateSourcingTripRequest request, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.FindTrip(id)
            .Include(item => item.Items)
            .Include(item => item.Stops)
            .SingleOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return AppResult<SourcingTripDto>.NotFound();
        }

        string? validation = ValidateTrip(request.Name, request.CashBudget);
        if (validation is not null)
        {
            return AppResult<SourcingTripDto>.Validation(validation);
        }

        trip.AssignedToUserId = request.AssignedToUserId;
        trip.Name = request.Name.Trim();
        trip.StartedAt = request.StartedAt ?? trip.StartedAt;
        trip.EndedAt = request.EndedAt;
        trip.RouteDate = request.RouteDate ?? trip.RouteDate;
        trip.PlannedStartAt = request.PlannedStartAt ?? trip.PlannedStartAt;
        trip.PlannedEndAt = request.PlannedEndAt;
        trip.RouteType = request.RouteType ?? trip.RouteType;
        trip.Status = request.Status ?? trip.Status;
        trip.CashBudget = request.CashBudget;
        trip.Notes = TrimToNull(request.Notes);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingTripDto>.Ok(trip.ToDto());
    }

    public async Task<AppResult<SourcingTripDto>> UpdateStatusAsync(Guid id, UpdateSourcingRouteStatusRequest request, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.FindTrip(id)
            .Include(item => item.Items)
            .Include(item => item.Stops)
            .SingleOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return AppResult<SourcingTripDto>.NotFound();
        }

        ApplyRouteStatus(trip, request.Status);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingTripDto>.Ok(trip.ToDto());
    }

    public async Task<AppResult<SourcingRouteDetailDto>> StartAsync(Guid id, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.LoadRouteAsync(id, cancellationToken);
        if (trip is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        trip.Status = SourcingRouteStatus.Active;
        trip.StartedAt = DateTimeOffset.UtcNow;
        trip.PlannedStartAt ??= trip.StartedAt;

        foreach (SourcingTripStop stop in trip.Stops.Where(item => item.Status == SourcingStopStatus.AtRisk))
        {
            stop.Status = SourcingStopStatus.Planned;
        }

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingRouteDetailDto>.Ok(SourcingTripQueryService.BuildRouteDetail(trip));
    }

    public async Task<AppResult<SourcingRouteDetailDto>> PauseAsync(Guid id, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.LoadRouteAsync(id, cancellationToken);
        if (trip is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        trip.Status = SourcingRouteStatus.Paused;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingRouteDetailDto>.Ok(SourcingTripQueryService.BuildRouteDetail(trip));
    }

    public async Task<AppResult<SourcingRouteDetailDto>> CompleteRouteAsync(Guid id, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.LoadRouteAsync(id, cancellationToken);
        if (trip is null)
        {
            return AppResult<SourcingRouteDetailDto>.NotFound();
        }

        trip.Status = SourcingRouteStatus.Completed;
        trip.EndedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<SourcingRouteDetailDto>.Ok(SourcingTripQueryService.BuildRouteDetail(trip));
    }

    /// <summary>Attach an inventory item to the trip (and optionally the exact stop it was bought at).</summary>
    public async Task<AppResult> AddItemAsync(Guid id, AddItemToTripRequest request, CancellationToken cancellationToken)
    {
        bool tripExists = await queries.FindTrip(id).AnyAsync(cancellationToken);
        if (!tripExists)
        {
            return AppResult.NotFound("Trip was not found.");
        }

        if (request.SourcingTripStopId.HasValue)
        {
            bool stopExists = await db.SourcingTripStops.AnyAsync(
                stop => stop.Id == request.SourcingTripStopId.Value
                        && stop.SourcingTripId == id
                        && stop.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (!stopExists)
            {
                return AppResult.Validation("Stop was not found for this trip.");
            }
        }

        InventoryItem? item = await db.InventoryItems.SingleOrDefaultAsync(
            inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (item is null)
        {
            return AppResult.NotFound("Inventory item was not found.");
        }

        item.SourcingTripId = id;
        item.SourcingTripStopId = request.SourcingTripStopId;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await queries.FindTrip(id).SingleOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return AppResult.NotFound();
        }

        db.SourcingTrips.Remove(trip);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private static void ApplyRouteStatus(SourcingTrip trip, SourcingRouteStatus status)
    {
        trip.Status = status;
        if (status == SourcingRouteStatus.Active)
        {
            trip.StartedAt = DateTimeOffset.UtcNow;
            trip.PlannedStartAt ??= trip.StartedAt;
        }

        if (status is SourcingRouteStatus.Completed or SourcingRouteStatus.Cancelled)
        {
            trip.EndedAt ??= DateTimeOffset.UtcNow;
        }
    }

    private static string? ValidateTrip(string name, decimal? cashBudget)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Trip name is required.";
        }

        if (cashBudget is < 0)
        {
            return "Cash budget cannot be negative.";
        }

        return null;
    }
}
