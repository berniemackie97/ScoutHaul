using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The home screen's data.
///
/// Gated on membership rather than one permission: the console is assembled from several areas and
/// shows each only to a caller who may open it — see <see cref="OperatorConsoleService"/>.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[RequireOrganizationMember]
public sealed class DashboardController(OperatorConsoleService console) : HaulScoutControllerBase
{
    /// <summary>What is waiting, what the stock is worth, and how trading is going.</summary>
    [HttpGet("console")]
    public async Task<ActionResult<OperatorConsoleDto>> Console(CancellationToken cancellationToken) =>
        await console.BuildAsync(cancellationToken);

    /// <summary>
    /// The summary the mobile app reads. Same shape and the same gate as before; now the same
    /// definitions as the console.
    /// </summary>
    [HttpGet("summary")]
    [RequireOrganizationPermission(HaulScoutPermissions.ViewReports)]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken cancellationToken) =>
        await console.LegacySummaryAsync(cancellationToken);
}
