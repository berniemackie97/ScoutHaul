using Microsoft.Extensions.Options;
using HaulScout.Application.Storefront;
using Stripe;
using Stripe.Checkout;

namespace HaulScout.Infrastructure.Storefront;

/// <summary>Binds the "Stripe" configuration section. Values come from environment variables, never source.</summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>
    /// The server-side key: a restricted key (rk_…, needs Checkout Sessions + Refunds + PaymentIntents read) or a
    /// secret key (sk_…). Named ApiKey to match the other providers' config (Gemini__ApiKey, GoogleCloudVision__ApiKey).
    /// Server-side Stripe secret key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Browser-safe key used by Stripe.js for Embedded Checkout.</summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>The webhook endpoint's signing secret (whsec_…). Required before webhooks are trusted.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}

/// <summary>
/// The Stripe implementation of <see cref="IPaymentGateway"/> — the only file in the system where Stripe types
/// appear. Stripe Checkout Sessions; dynamic payment methods (no payment_method_types, per Stripe guidance);
/// signature-verified webhooks reduced to neutral <see cref="PaymentEvent"/>s.
/// </summary>
public sealed class StripePaymentGateway(IOptions<StripeOptions> options) : IPaymentGateway
{
    /// <summary>Tags sessions in the Stripe dashboard so this flow's conversion can be told apart from any other.</summary>
    private const string IntegrationIdentifier = "scouthaul-storefront-kqzmwrvt";

    private readonly StripeOptions _options = options.Value;
    private StripeClient? _client;

    /// <summary>
    /// Built on first use, not at construction. Stripe's client throws on an empty key, and this type is resolved by
    /// the DI container the moment ANY storefront commerce endpoint is touched — so constructing it eagerly turned a
    /// missing setting into an opaque 500 from the container, before a single line of our code ran. Now a missing key
    /// fails where it is used, saying which setting is absent.
    /// </summary>
    private StripeClient Client => _client ??= string.IsNullOrWhiteSpace(_options.ApiKey)
        ? throw new PaymentGatewayNotConfiguredException(
            $"Stripe is not configured: set {StripeOptions.SectionName}__ApiKey (a sk_/rk_ server key) in the environment.")
        : new StripeClient(_options.ApiKey);

    public async Task<HostedCheckout> CreateCheckoutAsync(HostedCheckoutRequest request, CancellationToken cancellationToken)
    {
        List<SessionShippingOptionOptions> shippingOptions =
        [
            new()
            {
                ShippingRateData = new SessionShippingOptionShippingRateDataOptions
                {
                    Type = "fixed_amount",
                    DisplayName = request.Shipping.Label,
                    TaxBehavior = "exclusive",
                    FixedAmount = new SessionShippingOptionShippingRateDataFixedAmountOptions
                    {
                        Amount = request.Shipping.AmountCents,
                        Currency = "usd",
                    },
                    DeliveryEstimate = new SessionShippingOptionShippingRateDataDeliveryEstimateOptions
                    {
                        Minimum = new SessionShippingOptionShippingRateDataDeliveryEstimateMinimumOptions
                        {
                            Unit = "business_day",
                            Value = request.Shipping.EstimateMinDays,
                        },
                        Maximum = new SessionShippingOptionShippingRateDataDeliveryEstimateMaximumOptions
                        {
                            Unit = "business_day",
                            Value = request.Shipping.EstimateMaxDays,
                        },
                    },
                },
            },
        ];

        if (request.OfferLocalPickup)
        {
            shippingOptions.Add(new SessionShippingOptionOptions
            {
                ShippingRateData = new SessionShippingOptionShippingRateDataOptions
                {
                    Type = "fixed_amount",
                    DisplayName = "Local pickup (free)",
                    TaxBehavior = "exclusive",
                    FixedAmount = new SessionShippingOptionShippingRateDataFixedAmountOptions { Amount = 0, Currency = "usd" },
                    // The webhook reads this back to set Order.IsLocalPickup — display names are not contracts.
                    Metadata = new Dictionary<string, string> { ["local_pickup"] = "true" },
                },
            });
        }

        bool embedded = request.PreferEmbedded && !string.IsNullOrWhiteSpace(_options.PublishableKey);

        SessionCreateOptions options = new()
        {
            Mode = "payment",
            ClientReferenceId = request.OrderId.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["order_id"] = request.OrderId.ToString(),
                ["order_number"] = request.OrderNumber,
            },
            LineItems = request.Lines
                .Select(line => new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = line.UnitAmountCents,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = line.Title,
                            Images = line.ImageUrl is null ? null : [line.ImageUrl],
                        },
                    },
                })
                .ToList(),
            ShippingAddressCollection = new SessionShippingAddressCollectionOptions
            {
                AllowedCountries = request.AllowedShipCountries.ToList(),
            },
            ShippingOptions = shippingOptions,
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = request.AutomaticTax },
            ExpiresAt = request.ExpiresAt.UtcDateTime,
        };
        if (embedded)
        {
            options.UiMode = "embedded_page";
            options.ReturnUrl = request.ReturnUrl;
            options.RedirectOnCompletion = "always";
        }
        else
        {
            options.SuccessUrl = request.SuccessUrl;
            options.CancelUrl = request.CancelUrl;
        }
        options.AddExtraParam("integration_identifier", IntegrationIdentifier);

        try
        {
            Session session = await new SessionService(Client).CreateAsync(options, cancellationToken: cancellationToken);
            HostedCheckout checkout = ToHostedCheckout(session, embedded);
            if (embedded && string.IsNullOrWhiteSpace(checkout.ClientSecret))
            {
                throw new PaymentGatewayUnavailableException("Stripe did not return an embedded checkout client secret.");
            }

            return checkout;
        }
        catch (StripeException exception)
        {
            throw new PaymentGatewayUnavailableException("Stripe could not create the checkout session.", exception);
        }
    }

    public async Task<HostedCheckout?> FindOpenCheckoutAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            Session session = await new SessionService(Client).GetAsync(sessionId, cancellationToken: cancellationToken);
            return session.Status == "open" ? ToHostedCheckout(session, session.ClientSecret is not null) : null;
        }
        catch (StripeException)
        {
            return null;   // Unknown/foreign session — treat as not resumable and let checkout start fresh.
        }
    }

    public async Task<PaymentCapture> GetCaptureAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        PaymentIntent intent = await new PaymentIntentService(Client).GetAsync(
            paymentIntentId,
            new PaymentIntentGetOptions { Expand = ["latest_charge.balance_transaction"] },
            cancellationToken: cancellationToken);

        return new PaymentCapture(
            intent.Id,
            intent.AmountReceived,
            intent.LatestCharge?.BalanceTransaction?.Fee);
    }

    public async Task RefundAsync(string paymentIntentId, string reason, CancellationToken cancellationToken) =>
        await new RefundService(Client).CreateAsync(
            new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId,
                Metadata = new Dictionary<string, string> { ["reason"] = reason },
            },
            cancellationToken: cancellationToken);

    public async Task<PaymentEvent> ParseEventAsync(string payload, string signatureHeader, CancellationToken cancellationToken)
    {
        // No secret means we CANNOT verify, which is not the same as failing verification. Failing would drop the
        // event; this is a misconfiguration, so it must be loud and retryable — Stripe redelivers, and the event
        // lands once the secret is set.
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            throw new PaymentGatewayNotConfiguredException(
                $"Cannot verify Stripe webhooks: set {StripeOptions.SectionName}__WebhookSecret (whsec_…) from the endpoint's signing secret.");
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret);
        }
        catch (Exception exception) when (exception is StripeException or ArgumentException or FormatException)
        {
            // A malformed or absent signature header is a rejected request, never a server fault.
            throw new PaymentEventVerificationException("Webhook signature verification failed.", exception);
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            return Unhandled(stripeEvent.Id);
        }

        return stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted => await CompletedAsync(session, cancellationToken),
            EventTypes.CheckoutSessionExpired => new PaymentEvent(
                PaymentEventKind.CheckoutExpired, session.Id, OrderIdFrom(session), session.PaymentIntentId,
                null, null, null, null, null, null, null, false),
            _ => Unhandled(session.Id),
        };
    }

    private async Task<PaymentEvent> CompletedAsync(Session session, CancellationToken cancellationToken)
    {
        bool localPickup = false;
        if (session.ShippingCost?.ShippingRateId is string rateId)
        {
            // The chosen rate arrives as an id; its metadata says whether it was the pickup option.
            ShippingRate rate = await new ShippingRateService(Client).GetAsync(rateId, cancellationToken: cancellationToken);
            localPickup = rate.Metadata.TryGetValue("local_pickup", out string? flag) && flag == "true";
        }

        var address = session.CollectedInformation?.ShippingDetails?.Address;
        ShipTo? shipTo = address is null
            ? null
            : new ShipTo(
                session.CollectedInformation?.ShippingDetails?.Name,
                address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country);

        return new PaymentEvent(
            PaymentEventKind.CheckoutCompleted,
            session.Id,
            OrderIdFrom(session),
            session.PaymentIntentId,
            session.AmountTotal,
            session.AmountSubtotal,
            session.TotalDetails?.AmountTax,
            session.TotalDetails?.AmountShipping,
            session.CustomerDetails?.Email,
            session.CustomerDetails?.Name,
            shipTo,
            localPickup);
    }

    private static PaymentEvent Unhandled(string id) =>
        new(PaymentEventKind.Unhandled, id, null, null, null, null, null, null, null, null, null, false);

    private static Guid? OrderIdFrom(Session session) =>
        session.Metadata.TryGetValue("order_id", out string? raw) && Guid.TryParse(raw, out Guid id) ? id : null;

    private HostedCheckout ToHostedCheckout(Session session, bool embedded) => new(
        session.Id,
        session.Url,
        embedded ? session.ClientSecret : null,
        embedded ? _options.PublishableKey : null);
}
