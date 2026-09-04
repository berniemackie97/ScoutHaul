using Microsoft.AspNetCore.Mvc;
using HaulScout.Application.Contracts;
using HaulScout.Application.Sessions;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/session")]
public sealed class SessionController(SessionBootstrapService bootstrap) : HaulScoutControllerBase
{
    [HttpGet("bootstrap")]
    public async Task<ActionResult<SessionBootstrapDto>> Bootstrap(CancellationToken cancellationToken) =>
        await bootstrap.BuildAsync(cancellationToken);
}
