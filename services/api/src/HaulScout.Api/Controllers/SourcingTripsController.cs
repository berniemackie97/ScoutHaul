using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/sourcing-trips")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageRoutes)]
public sealed class SourcingTripsController(
    SourcingTripQueryService queries,
    SourcingTripWorkflowService trips,
    SourcingTripStopWorkflowService stops) : HaulScoutControllerBase
{
    // ── Trip ───────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<SourcingTripDto>> Create(CreateSourcingTripRequest request, CancellationToken cancellationToken)
    {
        AppResult<SourcingTripDto> result = await trips.CreateAsync(request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => CreatedAtAction(nameof(GetSummary), new { id = result.Value.Id }, result.Value),
            _ => ToActionResult(result),
        };
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SourcingTripDto>> Update(Guid id, UpdateSourcingTripRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await trips.UpdateAsync(id, request, cancellationToken));

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SourcingTripDto>> UpdateStatus(Guid id, UpdateSourcingRouteStatusRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await trips.UpdateStatusAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<SourcingRouteDetailDto>> Start(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await trips.StartAsync(id, cancellationToken));

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<SourcingRouteDetailDto>> Pause(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await trips.PauseAsync(id, cancellationToken));

    [HttpPost("{id:guid}/complete-route")]
    public async Task<ActionResult<SourcingRouteDetailDto>> CompleteRoute(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await trips.CompleteRouteAsync(id, cancellationToken));

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItem(Guid id, AddItemToTripRequest request, CancellationToken cancellationToken) =>
        ToNoContentResult(await trips.AddItemAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToNoContentResult(await trips.DeleteAsync(id, cancellationToken));

    // ── Reads ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SourcingTripDto>>> List(CancellationToken cancellationToken) =>
        Ok(await queries.ListAsync(cancellationToken));

    [HttpGet("{id:guid}/route")]
    public async Task<ActionResult<SourcingRouteDetailDto>> GetRoute(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await queries.GetRouteAsync(id, cancellationToken));

    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<SourcingTripSummaryDto>> GetSummary(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await queries.GetSummaryAsync(id, cancellationToken));

    // ── Stops ──────────────────────────────────────────────────────────────
    [HttpPost("{id:guid}/stops")]
    public async Task<ActionResult<SourcingTripStopDto>> CreateStop(Guid id, UpsertSourcingTripStopRequest request, CancellationToken cancellationToken)
    {
        AppResult<SourcingTripStopDto> result = await stops.CreateAsync(id, request, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => CreatedAtAction(nameof(GetRoute), new { id }, result.Value),
            _ => ToActionResult(result),
        };
    }

    [HttpPut("{id:guid}/stops/{stopId:guid}")]
    public async Task<ActionResult<SourcingTripStopDto>> UpdateStop(Guid id, Guid stopId, UpsertSourcingTripStopRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await stops.UpdateAsync(id, stopId, request, cancellationToken));

    [HttpDelete("{id:guid}/stops/{stopId:guid}")]
    public async Task<IActionResult> DeleteStop(Guid id, Guid stopId, CancellationToken cancellationToken) =>
        ToNoContentResult(await stops.DeleteAsync(id, stopId, cancellationToken));

    [HttpPost("{id:guid}/stops/reorder")]
    public async Task<ActionResult<SourcingRouteDetailDto>> ReorderStops(Guid id, ReorderSourcingTripStopsRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await stops.ReorderAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/stops/{stopId:guid}/apply-checklist-template")]
    public async Task<ActionResult<SourcingTripStopDto>> ApplyTemplate(Guid id, Guid stopId, CancellationToken cancellationToken) =>
        ToActionResult(await stops.ApplyChecklistTemplateAsync(id, stopId, cancellationToken));

    [HttpPost("{id:guid}/stops/{stopId:guid}/navigate")]
    public async Task<ActionResult<SourcingRouteDetailDto>> NavigateStop(Guid id, Guid stopId, CancellationToken cancellationToken) =>
        ToActionResult(await stops.NavigateAsync(id, stopId, cancellationToken));

    [HttpPost("{id:guid}/stops/{stopId:guid}/arrive")]
    public async Task<ActionResult<SourcingRouteDetailDto>> ArriveAtStop(Guid id, Guid stopId, CancellationToken cancellationToken) =>
        ToActionResult(await stops.ArriveAsync(id, stopId, cancellationToken));

    [HttpPost("{id:guid}/stops/{stopId:guid}/complete")]
    public async Task<ActionResult<SourcingRouteDetailDto>> CompleteStop(Guid id, Guid stopId, CompleteSourcingStopRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await stops.CompleteAsync(id, stopId, request, cancellationToken));

    [HttpPost("{id:guid}/stops/{stopId:guid}/skip")]
    public async Task<ActionResult<SourcingRouteDetailDto>> SkipStop(Guid id, Guid stopId, SkipSourcingStopRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await stops.SkipAsync(id, stopId, request, cancellationToken));

    [HttpPost("{id:guid}/stops/{stopId:guid}/checklist")]
    public async Task<ActionResult<SourcingStopChecklistItemDto>> CreateChecklistItem(Guid id, Guid stopId, CreateChecklistItemRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await stops.CreateChecklistItemAsync(id, stopId, request, cancellationToken));

    [HttpPatch("{id:guid}/stops/{stopId:guid}/checklist/{checklistItemId:guid}")]
    public async Task<ActionResult<SourcingRouteDetailDto>> UpdateChecklistItem(
        Guid id,
        Guid stopId,
        Guid checklistItemId,
        UpdateChecklistItemRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await stops.UpdateChecklistItemAsync(id, stopId, checklistItemId, request, cancellationToken));


    private IActionResult ToNoContentResult(AppResult result) =>
        result.Status switch
        {
            AppResultStatus.Ok => NoContent(),
            // A message-less NotFound stays a bare 404 — adding a null body would change the response shape.
            AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            _ => BadRequest(result.Message),
        };
}
