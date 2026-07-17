namespace HaulScout.Application.Contracts;

// The public commerce surface: what an anonymous shopper's browser sees. Never cost, margin, or supplier data —
// the same rule as the catalog DTOs in StoreContracts.

public sealed record StorefrontCartLineDto(
    Guid StoreListingId,
    string Slug,
    string Title,
    decimal UnitPrice,
    string? ImageUrl,
    /// <summary>False when the listing stopped being purchasable after it was added (sold/reserved elsewhere).
    /// The cart shows why checkout is refused instead of failing opaquely.</summary>
    bool IsAvailable);

public sealed record StorefrontCartDto(
    string CartToken,
    IReadOnlyList<StorefrontCartLineDto> Lines,
    decimal Subtotal,
    /// <summary>Order value at which shipping becomes free; null when no threshold is configured.</summary>
    decimal? FreeShippingThreshold,
    bool QualifiesForFreeShipping);

public sealed record AddCartLineRequest(Guid StoreListingId);

/// <summary>Where to send the shopper: the Stripe-hosted payment page.</summary>
public sealed record StorefrontCheckoutResultDto(string CheckoutUrl);

/// <summary>The confirmation page's order summary, looked up by the unguessable Stripe session id.</summary>
public sealed record StorefrontOrderSummaryDto(
    string OrderNumber,
    string Status,
    decimal Subtotal,
    decimal ShippingTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    bool IsLocalPickup,
    string? Email,
    IReadOnlyList<StorefrontOrderSummaryLineDto> Lines);

public sealed record StorefrontOrderSummaryLineDto(string Title, decimal UnitPrice, string? ImageUrl);
