using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;

namespace HaulScout.Api.Controllers;

/// <summary>The work inbox: every open task in lane order, and the ones set aside with what they wait on.</summary>
[ApiController]
[Route("api/work")]
[RequireOrganizationMember]
public sealed class WorkController(WorkInboxService work) : HaulScoutControllerBase
{
    [HttpGet]
    public Task<WorkInboxDto> Get(CancellationToken cancellationToken) => work.BuildAsync(cancellationToken);

    [HttpPost("set-aside")]
    public async Task<IActionResult> SetAside(SetAsideWorkTaskRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await work.SetAsideAsync(request, cancellationToken));

    [HttpPost("bring-back")]
    public async Task<IActionResult> BringBack([FromQuery] string key, CancellationToken cancellationToken) =>
        ToActionResult(await work.BringBackAsync(key, cancellationToken));
}
