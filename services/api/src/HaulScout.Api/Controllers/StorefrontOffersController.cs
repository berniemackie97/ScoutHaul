using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;

namespace HaulScout.Api.Controllers;

/// <summary>Public buyer offers. Captures buyer intent only; checkout is still the only reservation/payment path.</summary>
[ApiController]
[Route("api/storefront/offers")]
[AllowAnonymous]
public sealed class StorefrontOffersController(
    StorefrontStoreResolver resolver,
    StorefrontOfferWorkflowService offers) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<StorefrontOfferResultDto>> Submit(
        StorefrontOfferRequest request,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return BadRequest("Unknown store.");
        }

        return ToActionResult(await offers.SubmitAsync(organizationId, request, cancellationToken));
    }

    private ActionResult<T> ToActionResult<T>(AppResult<T> result) where T : class =>
        result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
}
