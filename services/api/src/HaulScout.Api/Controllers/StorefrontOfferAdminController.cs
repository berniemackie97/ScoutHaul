using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Storefront;
using HaulScout.Domain;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/storefront/offers")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageOrders)]
public sealed class StorefrontOfferAdminController(
    ICurrentUserContext currentUser,
    StorefrontOfferWorkflowService offers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StorefrontOfferDto>>> List(
        [FromQuery] StorefrontOfferStatus? status,
        CancellationToken cancellationToken)
    {
        return Ok(await offers.ListAsync(currentUser.OrganizationId, status, cancellationToken));
    }
}
