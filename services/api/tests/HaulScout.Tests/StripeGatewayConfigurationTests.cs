using Microsoft.Extensions.Options;
using HaulScout.Application.Storefront;
using HaulScout.Infrastructure.Storefront;

namespace HaulScout.Tests;

/// <summary>
/// How the gateway behaves when its settings are missing — which is a real state, not a hypothetical: the endpoint
/// goes live on deploy, but its signing secret cannot be created until Stripe can see that endpoint. So there is
/// always a window where the webhook exists and the secret does not, and it must fail in a way that says so and
/// loses nothing.
///
/// These exist because the first deploy 500'd from deep inside the DI container: Stripe's client throws on an empty
/// key at construction, so every commerce endpoint failed before any of our code ran, saying nothing about why.
/// </summary>
public class StripeGatewayConfigurationTests
{
    private static StripePaymentGateway Gateway(string apiKey = "sk_test_x", string webhookSecret = "whsec_x") =>
        new(Options.Create(new StripeOptions { ApiKey = apiKey, WebhookSecret = webhookSecret }));

    /// <summary>Constructing must never throw — the container builds this for every storefront commerce request.</summary>
    [Fact]
    public void A_gateway_with_no_key_still_constructs()
    {
        Exception? thrown = Record.Exception(() => Gateway(apiKey: ""));

        Assert.Null(thrown);
    }

    [Fact]
    public async Task Using_a_gateway_with_no_key_names_the_setting_that_is_missing()
    {
        PaymentGatewayNotConfiguredException exception = await Assert.ThrowsAsync<PaymentGatewayNotConfiguredException>(
            () => Gateway(apiKey: "   ").RefundAsync("pi_1", "test", CancellationToken.None));

        Assert.Contains("Stripe__ApiKey", exception.Message);
    }

    /// <summary>
    /// Unverifiable is not the same as invalid. Without a secret we cannot check the signature at all, so this must
    /// be a configuration failure (loud, retryable) rather than a verification failure (which would drop the event).
    /// </summary>
    [Fact]
    public async Task A_webhook_arriving_before_the_secret_is_set_is_a_configuration_failure()
    {
        PaymentGatewayNotConfiguredException exception = await Assert.ThrowsAsync<PaymentGatewayNotConfiguredException>(
            () => Gateway(webhookSecret: "").ParseEventAsync("{}", "t=1,v1=abc", CancellationToken.None));

        Assert.Contains("Stripe__WebhookSecret", exception.Message);
    }

    /// <summary>A missing or malformed signature header is the caller's fault: rejected, never a 500.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("t=1,v1=deadbeef")]
    public async Task An_unsigned_or_malformed_webhook_is_a_verification_failure(string signatureHeader)
    {
        await Assert.ThrowsAsync<PaymentEventVerificationException>(
            () => Gateway().ParseEventAsync("{\"id\":\"evt_1\"}", signatureHeader, CancellationToken.None));
    }
}
