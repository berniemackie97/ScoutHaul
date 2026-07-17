namespace HaulScout.Application.Storefront;

/// <summary>
/// The storefront's commerce decisions, as configuration. Amounts are money the operator changes without a deploy
/// (Railway env vars bind here). The defaults are the decisions locked in docs/STOREFRONT_COMMERCE.md.
/// </summary>
public sealed class StorefrontCheckoutOptions
{
    public const string SectionName = "StorefrontCheckout";

    /// <summary>Flat shipping charged per order, in the store currency.</summary>
    public decimal FlatShippingAmount { get; set; } = 9.95m;

    /// <summary>Item subtotal at which shipping becomes free. Null disables the threshold.</summary>
    public decimal? FreeShippingThreshold { get; set; } = 150m;

    /// <summary>Days shown to the shopper as the standard delivery estimate.</summary>
    public int ShippingEstimateMinDays { get; set; } = 3;
    public int ShippingEstimateMaxDays { get; set; } = 7;

    /// <summary>Offers a $0 "Local pickup" option at checkout; the order arrives flagged <c>IsLocalPickup</c>.</summary>
    public bool LocalPickupEnabled { get; set; } = true;

    /// <summary>
    /// Enables Stripe Tax on the session. Stripe only collects tax in jurisdictions with an ACTIVE registration —
    /// with none, it silently collects $0 (documented Stripe behaviour, not an error). Registrations live in the
    /// Stripe dashboard, not here.
    /// </summary>
    public bool AutomaticTaxEnabled { get; set; } = true;

    /// <summary>ISO country codes the store ships to.</summary>
    public string[] AllowedShipCountries { get; set; } = ["US"];

    /// <summary>Minutes before an unpaid checkout session expires and its reservations release. Stripe minimum 30.</summary>
    public int SessionExpiryMinutes { get; set; } = 30;

    /// <summary>
    /// The API's own public origin, used to give the payment page absolute product-image URLs. Null sends no
    /// images (checkout still works).
    /// </summary>
    public string? PublicApiBaseUrl { get; set; }

    /// <summary>
    /// Overrides the storefront's public base URL for success/cancel redirects. Null derives
    /// "https://{Organization.StoreDomain}" — the production default. Set locally (e.g. http://localhost:3000)
    /// so test checkouts land back on the dev storefront.
    /// </summary>
    public string? PublicBaseUrlOverride { get; set; }
}
