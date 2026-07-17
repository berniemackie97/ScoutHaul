namespace HaulScout.Application.Storefront;

/// <summary>
/// The payment processor, as the application understands it: it can host a checkout for an order, tell us what
/// happened, and give money back. Everything here is expressed in the store's own terms — no processor types cross
/// this boundary, so the concrete provider (Stripe today) is swappable and the use cases are testable with a fake.
/// Amounts are integer cents: the processor's unit, converted exactly once, at this seam.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a hosted checkout for the order and returns where to send the shopper.</summary>
    Task<HostedCheckout> CreateCheckoutAsync(HostedCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The live payment URL of a previously created checkout, or null once it has expired or completed — used to
    /// resume a shopper's own pending checkout instead of double-reserving against them.
    /// </summary>
    Task<string?> FindOpenCheckoutUrlAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>What the processor actually charged for a captured payment — the real fee for the books.</summary>
    Task<PaymentCapture> GetCaptureAsync(string paymentIntentId, CancellationToken cancellationToken);

    /// <summary>Returns the full payment. The conflict path: money arrived for an item we no longer hold.</summary>
    Task RefundAsync(string paymentIntentId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies a raw webhook against its signature and reduces it to a <see cref="PaymentEvent"/>. Verification
    /// failure throws <see cref="PaymentEventVerificationException"/> — an unverified payload never becomes an event.
    /// </summary>
    Task<PaymentEvent> ParseEventAsync(string payload, string signatureHeader, CancellationToken cancellationToken);
}

/// <summary>The webhook payload could not be proven to come from the processor. Reject the request; never act on it.</summary>
public sealed class PaymentEventVerificationException(string message, Exception? inner = null) : Exception(message, inner);

/// <param name="OrderId">Ours; round-trips through the processor's metadata so the webhook can find the order.</param>
/// <param name="ExpiresAt">When the session (and therefore the reservation) lapses.</param>
public sealed record HostedCheckoutRequest(
    Guid OrderId,
    string OrderNumber,
    IReadOnlyList<HostedCheckoutLine> Lines,
    ShippingChoice Shipping,
    bool OfferLocalPickup,
    bool AutomaticTax,
    string[] AllowedShipCountries,
    string SuccessUrl,
    string CancelUrl,
    DateTimeOffset ExpiresAt);

/// <param name="UnitAmountCents">Item price in cents.</param>
/// <param name="ImageUrl">Absolute URL, shown on the processor's payment page.</param>
public sealed record HostedCheckoutLine(string Title, long UnitAmountCents, string? ImageUrl);

/// <param name="AmountCents">What the shopper pays for shipping; 0 renders as free.</param>
public sealed record ShippingChoice(string Label, long AmountCents, int EstimateMinDays, int EstimateMaxDays);

public sealed record HostedCheckout(string SessionId, string Url);

/// <param name="FeeCents">The processor's fee for this capture; null when it could not be determined yet.</param>
public sealed record PaymentCapture(string PaymentIntentId, long AmountCents, long? FeeCents);

/// <summary>
/// A processor event the application acts on, already verified and reduced to what matters. Produced by the
/// Infrastructure adapter from the raw webhook (signature checking is the adapter's job — an unverified event
/// never reaches the application).
/// </summary>
public sealed record PaymentEvent(
    PaymentEventKind Kind,
    string SessionId,
    Guid? OrderId,
    string? PaymentIntentId,
    /// <summary>Total the shopper paid, in cents, for completed events.</summary>
    long? AmountTotalCents,
    /// <summary>Item subtotal in cents — checked against the order so a tampered/mismatched payment never finalizes.</summary>
    long? AmountSubtotalCents,
    long? TaxCents,
    long? ShippingCents,
    string? Email,
    string? CustomerName,
    ShipTo? ShipTo,
    bool LocalPickupChosen);

public enum PaymentEventKind
{
    /// <summary>Not an event this system acts on — acknowledged and ignored.</summary>
    Unhandled,
    CheckoutCompleted,
    CheckoutExpired,
}

public sealed record ShipTo(
    string? Name,
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country);
