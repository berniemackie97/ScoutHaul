using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Sync;

namespace HaulScout.Api.Controllers;

/// <summary>Sync issues as work a person can open, retry and close.</summary>
[ApiController]
[Route("api/sync/issues")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class SyncIssuesController(SyncIssueService issues) : HaulScoutControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SyncIssueGroupDto>>> ListOpen(CancellationToken cancellationToken) =>
        ToActionResult(await issues.ListOpenAsync(cancellationToken));

    [HttpGet("{id:guid}/snapshot")]
    public async Task<ActionResult<SyncIssueSnapshotDto>> Snapshot(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await issues.GetSnapshotAsync(id, cancellationToken));

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, ResolveSyncIssueRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await issues.ResolveAsync(id, request, cancellationToken));

    [HttpPost("resolve-group")]
    public async Task<ActionResult<int>> ResolveGroup(ResolveSyncIssueGroupRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await issues.ResolveGroupAsync(request, cancellationToken));

    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<RequestScanResultDto>> Retry(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await issues.RetryAsync(id, cancellationToken));
}
