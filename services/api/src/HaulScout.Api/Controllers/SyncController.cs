using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Sync.Agents;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The portal's sync hub: every account's sync state, schedules, bindings, scans on demand, and the
/// paired browsers. Channels are a listings concern, so the listings permission governs all of it.
/// </summary>
[ApiController]
[Route("api/sync")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class SyncController(SyncHubService hub) : HaulScoutControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<SyncOverviewDto>> Overview(CancellationToken cancellationToken) =>
        Ok(await hub.OverviewAsync(cancellationToken));

    [HttpPut("accounts/{accountId:guid}/schedule")]
    public async Task<ActionResult<SyncOverviewDto>> UpdateSchedule(Guid accountId, UpdateSyncScheduleRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await hub.UpdateScheduleAsync(accountId, request, cancellationToken));

    [HttpPut("accounts/{accountId:guid}/binding")]
    public async Task<ActionResult<SyncOverviewDto>> Bind(Guid accountId, BindSyncAccountRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await hub.BindAsync(accountId, request, cancellationToken));

    [HttpDelete("accounts/{accountId:guid}/binding")]
    public async Task<ActionResult<SyncOverviewDto>> Unbind(Guid accountId, CancellationToken cancellationToken) =>
        ToActionResult(await hub.UnbindAsync(accountId, cancellationToken));

    [HttpPost("scans")]
    public async Task<ActionResult<RequestScanResultDto>> RequestScan(RequestScanRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await hub.RequestScanAsync(request, cancellationToken));

    [HttpPost("scans/{requestId:guid}/cancel")]
    public async Task<IActionResult> CancelScan(Guid requestId, CancellationToken cancellationToken) =>
        ToActionResult(await hub.CancelScanAsync(requestId, cancellationToken));

    [HttpPatch("agents/{agentId:guid}")]
    public async Task<ActionResult<SyncAgentDto>> RenameAgent(Guid agentId, RenameSyncAgentRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await hub.RenameAgentAsync(agentId, request, cancellationToken));

    [HttpDelete("agents/{agentId:guid}")]
    public async Task<IActionResult> RevokeAgent(Guid agentId, CancellationToken cancellationToken) =>
        ToActionResult(await hub.RevokeAgentAsync(agentId, cancellationToken));

    [HttpPost("pairings")]
    public async Task<ActionResult<CreateSyncPairingResultDto>> CreatePairing(CancellationToken cancellationToken) =>
        Ok(await hub.CreatePairingAsync(cancellationToken));

    [HttpGet("pairings/{pairingId:guid}")]
    public async Task<ActionResult<SyncPairingDto>> GetPairing(Guid pairingId, CancellationToken cancellationToken) =>
        ToActionResult(await hub.GetPairingAsync(pairingId, cancellationToken));
}
