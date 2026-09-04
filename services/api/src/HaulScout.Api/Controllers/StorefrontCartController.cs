using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The public cart. Anonymous; the store comes from the domain header, the cart from an opaque token the storefront
/// keeps in an httpOnly cookie and forwards as a header. The token never appears in a URL.
/// </summary>
[ApiController]
[Route("api/storefront/cart")]
public sealed class StorefrontCartController(
    StorefrontStoreResolver resolver,
    StorefrontCustomerIdentityResolver customerIdentity,
    CartWorkflowService cart) : HaulScoutControllerBase
{
    private const string CartHeader = "X-HaulScout-Cart";

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<StorefrontCartDto>> Get(CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return Ok(await cart.GetAsync(organizationId, CartToken(), cancellationToken));
    }

    [HttpPost("lines")]
    [AllowAnonymous]
    public async Task<ActionResult<StorefrontCartDto>> AddLine(AddCartLineRequest request, CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return ToActionResult(await cart.AddLineAsync(organizationId, CartToken(), request, cancellationToken));
    }

    [HttpPost("offers/{offerId:guid}/line")]
    [Authorize(AuthenticationSchemes = StorefrontAuthDefaults.Scheme)]
    public async Task<ActionResult<StorefrontCartDto>> AddOfferLine(
        Guid offerId,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return ToActionResult(await cart.AddOfferLineAsync(
            organizationId,
            CartToken(),
            offerId,
            customerIdentity.Resolve(),
            cancellationToken));
    }

    [HttpDelete("lines/{storeListingId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<StorefrontCartDto>> RemoveLine(Guid storeListingId, CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return ToActionResult(await cart.RemoveLineAsync(organizationId, CartToken(), storeListingId, cancellationToken));
    }

    private string? CartToken() => Request.Headers[CartHeader].FirstOrDefault()?.Trim() is { Length: > 0 } token ? token : null;

}
