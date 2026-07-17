namespace HaulScout.Application.Storefront;

/// <summary>
/// The payment processor's settings are missing, so this operation cannot even be attempted. Distinct from a payment
/// FAILING: nothing was tried, nothing is wrong with the request, and the fix is a setting rather than a retry by the
/// shopper. Callers translate it to 503 (checkout is temporarily unavailable) or 500 (webhook — keep retrying), never
/// to a 4xx that would blame the caller for our configuration.
/// </summary>
public sealed class PaymentGatewayNotConfiguredException(string message) : Exception(message);
