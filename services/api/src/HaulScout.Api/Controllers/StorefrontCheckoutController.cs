using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;

namespace HaulScout.Api.Controllers;

/// <summary>Starts a checkout (reserve + hosted payment page) and serves the confirmation-page order summary.</summary>
[ApiController]
[Route("api/storefront")]
[AllowAnonymous]
public sealed class StorefrontCheckoutController(StorefrontStoreResolver resolver, CheckoutWorkflowService checkout) : ControllerBase
{
    private const string CartHeader = "X-HaulScout-Cart";

    [HttpPost("checkout")]
    public async Task<ActionResult<StorefrontCheckoutResultDto>> Checkout(CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        string? cartToken = Request.Headers[CartHeader].FirstOrDefault()?.Trim() is { Length: > 0 } token ? token : null;
        return ToActionResult(await checkout.CheckoutAsync(organizationId, cartToken, cancellationToken));
    }

    /// <summary>The session id is the lookup credential: unguessable, known only to the shopper Stripe redirected.</summary>
    [HttpGet("orders/by-session/{sessionId}")]
    public async Task<ActionResult<StorefrontOrderSummaryDto>> OrderBySession(string sessionId, CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(Request, cancellationToken) is not Guid organizationId)
        {
            return NotFound();
        }

        return ToActionResult(await checkout.GetOrderBySessionAsync(organizationId, sessionId, cancellationToken));
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
