using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/sourcing-trips")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageRoutes)]
public sealed class SourcingTripsController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SourcingTripDto>> Create(CreateSourcingTripRequest request, CancellationToken cancellationToken)
    {
        string? validation = ValidateTrip(request.Name, request.CashBudget);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset startedAt = request.StartedAt ?? request.PlannedStartAt ?? now;

        var trip = new SourcingTrip
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

        return CreatedAtAction(nameof(GetSummary), new { id = trip.Id }, trip.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SourcingTripDto>> Update(Guid id, UpdateSourcingTripRequest request, CancellationToken cancellationToken)
    {
        var trip = await FindTrip(id)
            .Include(item => item.Items)
            .Include(item => item.Stops)
            .SingleOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return NotFound();
        }

        string? validation = ValidateTrip(request.Name, request.CashBudget);
        if (validation is not null)
        {
            return BadRequest(validation);
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

        return trip.ToDto();
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SourcingTripDto>> UpdateStatus(Guid id, UpdateSourcingRouteStatusRequest request, CancellationToken cancellationToken)
    {
        var trip = await FindTrip(id)
            .Include(item => item.Items)
            .Include(item => item.Stops)
            .SingleOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return NotFound();
        }

        ApplyRouteStatus(trip, request.Status);
        await db.SaveChangesAsync(cancellationToken);

        return trip.ToDto();
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<SourcingRouteDetailDto>> Start(Guid id, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        trip.Status = SourcingRouteStatus.Active;
        trip.StartedAt = DateTimeOffset.UtcNow;
        trip.PlannedStartAt ??= trip.StartedAt;

        foreach (var stop in trip.Stops.Where(item => item.Status == SourcingStopStatus.AtRisk))
        {
            stop.Status = SourcingStopStatus.Planned;
        }

        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<SourcingRouteDetailDto>> Pause(Guid id, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        trip.Status = SourcingRouteStatus.Paused;
        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/complete-route")]
    public async Task<ActionResult<SourcingRouteDetailDto>> CompleteRoute(Guid id, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        trip.Status = SourcingRouteStatus.Completed;
        trip.EndedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItem(Guid id, AddItemToTripRequest request, CancellationToken cancellationToken)
    {
        var tripExists = await FindTrip(id).AnyAsync(cancellationToken);

        if (!tripExists)
        {
            return NotFound("Trip was not found.");
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
                return BadRequest("Stop was not found for this trip.");
            }
        }

        var item = await db.InventoryItems.SingleOrDefaultAsync(
            inventoryItem => inventoryItem.Id == request.InventoryItemId && inventoryItem.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (item is null)
        {
            return NotFound("Inventory item was not found.");
        }

        item.SourcingTripId = id;
        item.SourcingTripStopId = request.SourcingTripStopId;
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SourcingTripDto>>> List(CancellationToken cancellationToken)
    {
        var trips = await db.SourcingTrips
            .Include(item => item.Items)
            .Include(item => item.Stops)
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.PlannedStartAt ?? item.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return trips.Select(item => item.ToDto()).ToList();
    }

    [HttpGet("{id:guid}/route")]
    public async Task<ActionResult<SourcingRouteDetailDto>> GetRoute(Guid id, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        return trip is null ? NotFound() : BuildRouteDetail(trip);
    }

    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<SourcingTripSummaryDto>> GetSummary(Guid id, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);

        if (trip is null)
        {
            return NotFound();
        }

        return BuildSummary(trip);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var trip = await FindTrip(id).SingleOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        db.SourcingTrips.Remove(trip);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/stops")]
    public async Task<ActionResult<SourcingTripStopDto>> CreateStop(Guid id, UpsertSourcingTripStopRequest request, CancellationToken cancellationToken)
    {
        var trip = await FindTrip(id)
            .Include(item => item.Stops)
            .SingleOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return NotFound();
        }

        string? validation = ValidateStop(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        int nextOrder = trip.Stops.Count == 0 ? 1 : trip.Stops.Max(item => item.PlannedOrder) + 1;
        var stop = new SourcingTripStop
        {
            OrganizationId = currentUser.OrganizationId,
            SourcingTripId = trip.Id,
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            PlannedOrder = request.PlannedOrder ?? nextOrder
        };

        Apply(request, stop);
        ApplyChecklistTemplate(stop);
        db.SourcingTripStops.Add(stop);
        trip.Status = trip.Status == SourcingRouteStatus.Draft ? SourcingRouteStatus.Planned : trip.Status;
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetRoute), new { id }, stop.ToDto());
    }

    [HttpPut("{id:guid}/stops/{stopId:guid}")]
    public async Task<ActionResult<SourcingTripStopDto>> UpdateStop(Guid id, Guid stopId, UpsertSourcingTripStopRequest request, CancellationToken cancellationToken)
    {
        var stop = await db.SourcingTripStops
            .Include(item => item.Items)
            .Include(item => item.ChecklistItems)
            .SingleOrDefaultAsync(
                item => item.Id == stopId && item.SourcingTripId == id && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

        if (stop is null)
        {
            return NotFound();
        }

        string? validation = ValidateStop(request);
        if (validation is not null)
        {
            return BadRequest(validation);
        }

        stop.Name = request.Name.Trim();
        stop.Address = request.Address.Trim();
        Apply(request, stop);
        await db.SaveChangesAsync(cancellationToken);

        return stop.ToDto();
    }

    [HttpDelete("{id:guid}/stops/{stopId:guid}")]
    public async Task<IActionResult> DeleteStop(Guid id, Guid stopId, CancellationToken cancellationToken)
    {
        var stop = await db.SourcingTripStops
            .SingleOrDefaultAsync(
                item => item.Id == stopId && item.SourcingTripId == id && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

        if (stop is null)
        {
            return NotFound();
        }

        db.SourcingTripStops.Remove(stop);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/stops/reorder")]
    public async Task<ActionResult<SourcingRouteDetailDto>> ReorderStops(Guid id, ReorderSourcingTripStopsRequest request, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        var requestedIds = request.StopIds.ToHashSet();
        if (requestedIds.Count != trip.Stops.Count || trip.Stops.Any(item => !requestedIds.Contains(item.Id)))
        {
            return BadRequest("Reorder request must include every stop exactly once.");
        }

        for (int index = 0; index < request.StopIds.Count; index++)
        {
            var stop = trip.Stops.Single(item => item.Id == request.StopIds[index]);
            stop.PlannedOrder = index + 1;
        }

        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/stops/{stopId:guid}/apply-checklist-template")]
    public async Task<ActionResult<SourcingTripStopDto>> ApplyTemplate(Guid id, Guid stopId, CancellationToken cancellationToken)
    {
        var stop = await db.SourcingTripStops
            .Include(item => item.Items)
            .Include(item => item.ChecklistItems)
            .SingleOrDefaultAsync(
                item => item.Id == stopId && item.SourcingTripId == id && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

        if (stop is null)
        {
            return NotFound();
        }

        ApplyChecklistTemplate(stop);
        await db.SaveChangesAsync(cancellationToken);
        return stop.ToDto();
    }

    [HttpPost("{id:guid}/stops/{stopId:guid}/navigate")]
    public async Task<ActionResult<SourcingRouteDetailDto>> NavigateStop(Guid id, Guid stopId, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        var stop = FindStop(trip, stopId);

        if (trip is null || stop is null)
        {
            return NotFound();
        }

        trip.Status = SourcingRouteStatus.Active;
        stop.Status = SourcingStopStatus.Navigating;
        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/stops/{stopId:guid}/arrive")]
    public async Task<ActionResult<SourcingRouteDetailDto>> ArriveAtStop(Guid id, Guid stopId, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        var stop = FindStop(trip, stopId);

        if (trip is null || stop is null)
        {
            return NotFound();
        }

        trip.Status = SourcingRouteStatus.Active;
        stop.ActualArrivalAt ??= DateTimeOffset.UtcNow;
        stop.Status = SourcingStopStatus.InProgress;
        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/stops/{stopId:guid}/complete")]
    public async Task<ActionResult<SourcingRouteDetailDto>> CompleteStop(Guid id, Guid stopId, CompleteSourcingStopRequest request, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        var stop = FindStop(trip, stopId);

        if (trip is null || stop is null)
        {
            return NotFound();
        }

        bool hasIncompleteRequired = stop.ChecklistItems.Any(item => item.IsRequired && !item.IsCompleted);
        if (hasIncompleteRequired && !request.Force)
        {
            return BadRequest("Required checklist items are incomplete.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        stop.ActualArrivalAt ??= now;
        stop.ActualDepartureAt = now;
        stop.Status = SourcingStopStatus.Completed;

        if (trip.Stops.All(item => item.Status is SourcingStopStatus.Completed or SourcingStopStatus.Skipped or SourcingStopStatus.Cancelled))
        {
            trip.Status = SourcingRouteStatus.Completed;
            trip.EndedAt ??= now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/stops/{stopId:guid}/skip")]
    public async Task<ActionResult<SourcingRouteDetailDto>> SkipStop(Guid id, Guid stopId, SkipSourcingStopRequest request, CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        var stop = FindStop(trip, stopId);

        if (trip is null || stop is null)
        {
            return NotFound();
        }

        if (!stop.CanBeSkipped)
        {
            return BadRequest("This stop is marked as required.");
        }

        stop.Status = SourcingStopStatus.Skipped;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            stop.Notes = string.Join("\n", new[] { stop.Notes, $"Skip reason: {request.Reason.Trim()}" }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    [HttpPost("{id:guid}/stops/{stopId:guid}/checklist")]
    public async Task<ActionResult<SourcingStopChecklistItemDto>> CreateChecklistItem(Guid id, Guid stopId, CreateChecklistItemRequest request, CancellationToken cancellationToken)
    {
        var stop = await db.SourcingTripStops
            .Include(item => item.ChecklistItems)
            .SingleOrDefaultAsync(
                item => item.Id == stopId && item.SourcingTripId == id && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

        if (stop is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest("Checklist item text is required.");
        }

        var item = new SourcingStopChecklistItem
        {
            OrganizationId = currentUser.OrganizationId,
            SourcingTripStopId = stop.Id,
            Text = request.Text.Trim(),
            SortOrder = stop.ChecklistItems.Count == 0 ? 1 : stop.ChecklistItems.Max(checklistItem => checklistItem.SortOrder) + 1,
            IsRequired = request.IsRequired
        };

        db.SourcingStopChecklistItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item.ToDto();
    }

    [HttpPatch("{id:guid}/stops/{stopId:guid}/checklist/{checklistItemId:guid}")]
    public async Task<ActionResult<SourcingRouteDetailDto>> UpdateChecklistItem(
        Guid id,
        Guid stopId,
        Guid checklistItemId,
        UpdateChecklistItemRequest request,
        CancellationToken cancellationToken)
    {
        var trip = await LoadRoute(id, cancellationToken);
        var stop = FindStop(trip, stopId);
        var checklistItem = stop?.ChecklistItems.SingleOrDefault(item => item.Id == checklistItemId);

        if (trip is null || stop is null || checklistItem is null)
        {
            return NotFound();
        }

        checklistItem.IsCompleted = request.IsCompleted;
        checklistItem.CompletedAt = request.IsCompleted ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(cancellationToken);
        return BuildRouteDetail(trip);
    }

    private IQueryable<SourcingTrip> FindTrip(Guid id) =>
        db.SourcingTrips.Where(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId);

    private Task<SourcingTrip?> LoadRoute(Guid id, CancellationToken cancellationToken) =>
        FindTrip(id)
            .Include(item => item.Items)
            .Include(item => item.Stops)
                .ThenInclude(item => item.Items)
            .Include(item => item.Stops)
                .ThenInclude(item => item.ChecklistItems)
            .SingleOrDefaultAsync(cancellationToken);

    private static SourcingTripStop? FindStop(SourcingTrip? trip, Guid stopId) =>
        trip?.Stops.SingleOrDefault(item => item.Id == stopId);

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

    private static SourcingRouteDetailDto BuildRouteDetail(SourcingTrip trip) =>
        new(
            trip.ToDto(),
            BuildSummary(trip),
            trip.Stops
                .OrderBy(item => item.PlannedOrder)
                .ThenBy(item => item.CreatedAt)
                .Select(item => item.ToDto())
                .ToList());

    private static SourcingTripSummaryDto BuildSummary(SourcingTrip trip)
    {
        var stops = trip.Stops
            .Where(item => item.Status != SourcingStopStatus.Cancelled)
            .ToList();
        var completedStops = stops.Where(item => item.Status == SourcingStopStatus.Completed).ToList();
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

    private static void ApplyChecklistTemplate(SourcingTripStop stop)
    {
        string[] template = GetChecklistTemplate(stop.SourceType);
        HashSet<string> existing = stop.ChecklistItems.Select(item => item.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int nextOrder = stop.ChecklistItems.Count == 0 ? 1 : stop.ChecklistItems.Max(item => item.SortOrder) + 1;

        foreach (string text in template.Where(item => !existing.Contains(item)))
        {
            stop.ChecklistItems.Add(new SourcingStopChecklistItem
            {
                OrganizationId = stop.OrganizationId,
                Text = text,
                SortOrder = nextOrder++,
                IsRequired = true
            });
        }
    }

    private static string[] GetChecklistTemplate(SourcingStopSourceType sourceType) =>
        sourceType switch
        {
            SourcingStopSourceType.GarageSale => [
                "Ask if they have video games, electronics, tools, or baby items not outside",
                "Check shoes",
                "Check toys and LEGO",
                "Check baby monitors and baby gear",
                "Check small appliances",
                "Check tools",
                "Scan barcodes on boxed items",
                "Bundle-negotiate before leaving",
                "Add purchases before leaving"
            ],
            SourcingStopSourceType.EstateSale => [
                "Check garage/tools first",
                "Check electronics",
                "Check kitchen brands and small appliances",
                "Check baby/kids items",
                "Check books/media",
                "Look for boxed items/manuals",
                "Photograph model numbers",
                "Record high-value items passed on",
                "Consider return on discount day"
            ],
            SourcingStopSourceType.ThriftStore or SourcingStopSourceType.Goodwill => [
                "Check shoes",
                "Check electronics case",
                "Check small appliances",
                "Check toys",
                "Check bags",
                "Check sporting goods",
                "Check books/media",
                "Check baby/kids section",
                "Scan promising barcodes",
                "Record spend before checkout"
            ],
            SourcingStopSourceType.FacebookPickup => [
                "Confirm seller/item",
                "Inspect item condition",
                "Test power/function if applicable",
                "Confirm all accessories included",
                "Compare to listing photos",
                "Negotiate if condition differs",
                "Record purchase price",
                "Mark pickup complete"
            ],
            SourcingStopSourceType.Liquidation or SourcingStopSourceType.Auction => [
                "Check manifest if available",
                "Inspect box/item condition",
                "Estimate sellable percentage",
                "Check for missing parts",
                "Avoid restricted/unsafe categories",
                "Record lot cost and item count",
                "Photograph lot before sorting"
            ],
            _ => [
                "Confirm location and seller details",
                "Inspect condition before buying",
                "Check comps before committing",
                "Record purchase price",
                "Add purchases before leaving"
            ]
        };

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
