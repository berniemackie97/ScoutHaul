using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Application.Storefront;

namespace HaulScout.Api.Controllers;

/// <summary>
/// Stripe's callback. No store header and no tenant here — the event's session id finds the order, and the order
/// says whose it is. Signature verification happens in the gateway before anything is trusted; the HTTP answer is
/// chosen so Stripe's retry machinery works FOR us: 2xx stops retries, 5xx keeps them coming until a human looks.
/// </summary>
[ApiController]
[Route("api/storefront/stripe")]
[AllowAnonymous]
public sealed class StorefrontStripeWebhookController(
    IPaymentGateway payments,
    StorefrontOrderService orders,
    ILogger<StorefrontStripeWebhookController> logger) : ControllerBase
{
    [HttpPost("webhook")]
    public async Task<IActionResult> Handle(CancellationToken cancellationToken)
    {
        using StreamReader reader = new(Request.Body);
        string payload = await reader.ReadToEndAsync(cancellationToken);
        string signature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;

        PaymentEvent paymentEvent;
        try
        {
            paymentEvent = await payments.ParseEventAsync(payload, signature, cancellationToken);
        }
        catch (PaymentEventVerificationException exception)
        {
            logger.LogWarning(exception, "Rejected a webhook whose signature did not verify.");
            return BadRequest();
        }
        catch (PaymentGatewayNotConfiguredException exception)
        {
            // Never acknowledge an event we could not verify: 500 makes Stripe redeliver, so nothing is lost between
            // the endpoint going live and its signing secret being set.
            logger.LogError(exception, "A Stripe webhook arrived before the signing secret was configured.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        PaymentEventOutcome outcome = await orders.HandleAsync(paymentEvent, cancellationToken);
        logger.LogInformation(
            "Stripe event {Kind} for session {SessionId}: {Outcome}.",
            paymentEvent.Kind, paymentEvent.SessionId, outcome);

        return outcome switch
        {
            // A money disagreement is never acknowledged: 500 keeps Stripe retrying and the failure visible
            // until someone reconciles it by hand.
            PaymentEventOutcome.AmountMismatch => StatusCode(StatusCodes.Status500InternalServerError),
            _ => Ok(),
        };
    }
}
