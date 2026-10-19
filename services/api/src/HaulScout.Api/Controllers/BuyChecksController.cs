using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/buy-checks")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class BuyChecksController(BuyCheckService buyChecks) : HaulScoutControllerBase
{
    /// <summary>What an item nets on each channel the business sells on, and the most to pay for it.</summary>
    [HttpPost]
    public async Task<ActionResult<BuyCheckDto>> Check(BuyCheckRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await buyChecks.CheckAsync(request, cancellationToken));
}
