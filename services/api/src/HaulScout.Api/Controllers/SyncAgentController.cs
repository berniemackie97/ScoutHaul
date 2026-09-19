using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HaulScout.Api.Authentication;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Sync.Agents;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The sync agent's endpoints — called by the browser extension with its device token, never by a
/// person. Pairing is the one exception: it is how a browser gets a token in the first place.
/// </summary>
[ApiController]
[Route("api/sync/agent")]
[RequireSyncAgent]
public sealed class SyncAgentController(SyncAgentService sync) : HaulScoutControllerBase
{
    /// <summary>A batch is a few hundred records; nothing an agent sends legitimately comes near this.</summary>
    private const long MaxBatchBytes = 8 * 1024 * 1024;

    [HttpPost("pair")]
    [AllowAnonymous]
    [EnableRateLimiting(SyncAgentAuthDefaults.PairingRateLimitPolicy)]
    public async Task<ActionResult<PairAgentResultDto>> Pair(PairAgentRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await sync.PairAsync(request, cancellationToken));

    [HttpPost("heartbeat")]
    public async Task<ActionResult<AgentHeartbeatDto>> Heartbeat(AgentHeartbeatRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await sync.HeartbeatAsync(request, cancellationToken));

    [HttpPost("runs")]
    public async Task<ActionResult<AgentRunDto>> StartRun(StartAgentRunRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await sync.StartRunAsync(request, cancellationToken));

    [HttpPost("runs/{runId:guid}/batches")]
    [RequestSizeLimit(MaxBatchBytes)]
    public async Task<ActionResult<AgentRunBatchResultDto>> AddBatch(Guid runId, AgentRunBatchRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await sync.AddBatchAsync(runId, request, cancellationToken));

    [HttpPost("runs/{runId:guid}/complete")]
    public async Task<ActionResult<SyncRunDto>> CompleteRun(Guid runId, CompleteAgentRunRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await sync.CompleteRunAsync(runId, request, cancellationToken));

    [HttpPost("runs/{runId:guid}/fail")]
    public async Task<ActionResult<SyncRunDto>> FailRun(Guid runId, FailAgentRunRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await sync.FailRunAsync(runId, request, cancellationToken));
}
