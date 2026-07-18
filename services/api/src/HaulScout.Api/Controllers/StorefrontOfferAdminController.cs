using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Storefront;
using HaulScout.Domain;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/admin/storefront/offers")]
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

    [HttpPost("{offerId:guid}/decision")]
    public async Task<ActionResult<StorefrontOfferDto>> Decide(
        Guid offerId,
        StorefrontOfferDecisionRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<StorefrontOfferDto> result = await offers.DecideAsync(
            currentUser.OrganizationId,
            offerId,
            request,
            cancellationToken);

        return result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
    }
}
