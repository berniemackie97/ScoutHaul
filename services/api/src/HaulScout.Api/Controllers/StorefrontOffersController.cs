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
[Authorize(AuthenticationSchemes = StorefrontAuthDefaults.Scheme)]
public sealed class StorefrontOffersController(
    StorefrontStoreResolver resolver,
    StorefrontCustomerIdentityResolver customerIdentity,
    StorefrontOfferWorkflowService offers) : HaulScoutControllerBase
{
    [HttpPost]
    public async Task<ActionResult<StorefrontOfferResultDto>> Submit(
        StorefrontOfferRequest request,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return Invalid("Unknown store.");
        }

        return ToActionResult(await offers.SubmitAsync(
            organizationId,
            customerIdentity.Resolve(),
            request,
            cancellationToken));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StorefrontCustomerOfferDto>>> Mine(CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return Invalid("Unknown store.");
        }

        return Ok(await offers.ListCustomerAsync(organizationId, customerIdentity.Resolve(), cancellationToken));
    }

}
