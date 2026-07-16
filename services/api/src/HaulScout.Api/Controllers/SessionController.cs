using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/session")]
public sealed class SessionController(SessionBootstrapService bootstrap) : ControllerBase
{
    [HttpGet("bootstrap")]
    public async Task<ActionResult<SessionBootstrapDto>> Bootstrap(CancellationToken cancellationToken) =>
        await bootstrap.BuildAsync(cancellationToken);
}
