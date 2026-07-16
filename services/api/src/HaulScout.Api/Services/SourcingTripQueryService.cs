using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Reads of a sourcing route, plus the shared loaders and projections the route workflows build on: a route is a trip
/// with its stops, their checklists, and the items picked up along the way.
/// </summary>
public sealed class SourcingTripQueryService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public IQueryable<SourcingTrip> FindTrip(Guid id) =>
        db.SourcingTrips.Where(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId);

    public Task<SourcingTrip?> LoadRouteAsync(Guid id, CancellationToken cancellationToken) =>
        FindTrip(id)
            .Include(item => item.Items)
            .Include(item => item.Stops)
                .ThenInclude(item => item.Items)
            .Include(item => item.Stops)
                .ThenInclude(item => item.ChecklistItems)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Lightweight route load that skips inventory items on both the trip and its stops.
    /// Use for actions that only need stops and their checklists (navigate, arrive, complete, skip, checklist-update).
    /// </summary>
    public Task<SourcingTrip?> LoadRouteSlimAsync(Guid id, CancellationToken cancellationToken) =>
        FindTrip(id)
            .Include(item => item.Stops)
                .ThenInclude(item => item.ChecklistItems)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SourcingTripDto>> ListAsync(CancellationToken cancellationToken)
    {
        List<SourcingTrip> trips = await db.SourcingTrips
            .Include(item => item.Items)
            .Include(item => item.Stops)
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.PlannedStartAt ?? item.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return trips.Select(item => item.ToDto()).ToList();
    }

    public async Task<AppResult<SourcingRouteDetailDto>> GetRouteAsync(Guid id, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await LoadRouteAsync(id, cancellationToken);
        return trip is null
            ? AppResult<SourcingRouteDetailDto>.NotFound()
            : AppResult<SourcingRouteDetailDto>.Ok(BuildRouteDetail(trip));
    }

    public async Task<AppResult<SourcingTripSummaryDto>> GetSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        SourcingTrip? trip = await LoadRouteAsync(id, cancellationToken);
        return trip is null
            ? AppResult<SourcingTripSummaryDto>.NotFound()
            : AppResult<SourcingTripSummaryDto>.Ok(BuildSummary(trip));
    }

    public static SourcingTripStop? FindStop(SourcingTrip? trip, Guid stopId) =>
        trip?.Stops.SingleOrDefault(item => item.Id == stopId);

    public static SourcingRouteDetailDto BuildRouteDetail(SourcingTrip trip) =>
        new(
            trip.ToDto(),
            BuildSummary(trip),
            trip.Stops
                .OrderBy(item => item.PlannedOrder)
                .ThenBy(item => item.CreatedAt)
                .Select(item => item.ToDto())
                .ToList());

    public static SourcingTripSummaryDto BuildSummary(SourcingTrip trip)
    {
        List<SourcingTripStop> stops = trip.Stops
            .Where(item => item.Status != SourcingStopStatus.Cancelled)
            .ToList();
        List<SourcingTripStop> completedStops = stops.Where(item => item.Status == SourcingStopStatus.Completed).ToList();
        int plannedMinutes = stops.Sum(item => item.EstimatedDurationMinutes);
        int actualMinutes = completedStops.Sum(item =>
            item.ActualArrivalAt.HasValue && item.ActualDepartureAt.HasValue
                ? Math.Max(0, (int)Math.Round((item.ActualDepartureAt.Value - item.ActualArrivalAt.Value).TotalMinutes))
                : 0);
        int completedPlannedMinutes = completedStops.Sum(item => item.EstimatedDurationMinutes);
        decimal purchaseCost = trip.Items.Sum(item => item.PurchasePrice ?? 0);
        decimal askingPrice = trip.Items.Sum(item => item.SellerAskingPrice ?? 0);

        return new SourcingTripSummaryDto(
            trip.Id,
            trip.Name,
            trip.Items.Count,
            stops.Count,
            completedStops.Count,
            stops.Count(item => item.Status == SourcingStopStatus.Skipped),
            trip.Items.Count(item => item.PurchasePrice.HasValue),
            purchaseCost,
            askingPrice,
            trip.CashBudget,
            (trip.CashBudget ?? 0) - purchaseCost,
            plannedMinutes,
            actualMinutes,
            actualMinutes - completedPlannedMinutes,
            trip.Status);
    }
}
