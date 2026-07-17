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
[AllowAnonymous]
public sealed class StorefrontCartController(StorefrontStoreResolver resolver, CartWorkflowService cart) : ControllerBase
{
    private const string CartHeader = "X-HaulScout-Cart";

    [HttpGet]
    public async Task<ActionResult<StorefrontCartDto>> Get(CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return Ok(await cart.GetAsync(organizationId, CartToken(), cancellationToken));
    }

    [HttpPost("lines")]
    public async Task<ActionResult<StorefrontCartDto>> AddLine(AddCartLineRequest request, CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return ToActionResult(await cart.AddLineAsync(organizationId, CartToken(), request, cancellationToken));
    }

    [HttpDelete("lines/{storeListingId:guid}")]
    public async Task<ActionResult<StorefrontCartDto>> RemoveLine(Guid storeListingId, CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return ToActionResult(await cart.RemoveLineAsync(organizationId, CartToken(), storeListingId, cancellationToken));
    }

    private string? CartToken() => Request.Headers[CartHeader].FirstOrDefault()?.Trim() is { Length: > 0 } token ? token : null;

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
