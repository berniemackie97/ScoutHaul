using System.Globalization;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using Microsoft.Extensions.Options;

namespace HaulScout.Application.Storefront;

/// <summary>
/// Storefront shipping policy in one place. Today it produces Stripe fixed-rate options; carrier/rate-shopping can
/// replace this service later without checkout, cart, or admin inventing their own math.
/// </summary>
public sealed class StorefrontShippingCalculator(IOptions<StorefrontCheckoutOptions> options)
{
    private readonly StorefrontCheckoutOptions _options = options.Value;

    public ShippingQuote Quote(decimal subtotal, IEnumerable<StoreListing?> listings)
    {
        if (_options.FreeShippingThreshold is decimal threshold && subtotal >= threshold)
        {
            return Free("Free shipping");
        }

        StoreListing[] rows = listings.Where(listing => listing is not null).Cast<StoreListing>().ToArray();
        if (rows.Length > 0 && rows.All(listing => listing.ShippingPolicy == StoreShippingPolicy.Free))
        {
            return Free("Free shipping");
        }

        decimal amount = _options.FlatShippingAmount;
        decimal[] fixedAmounts = rows
            .Where(listing => listing.ShippingPolicy == StoreShippingPolicy.FixedAmount)
            .Select(listing => listing.ShippingAmountOverride ?? 0m)
            .ToArray();

        if (fixedAmounts.Length > 0)
        {
            decimal listingRate = fixedAmounts.Max();
            amount = rows.All(listing => listing.ShippingPolicy != StoreShippingPolicy.StoreDefault)
                ? listingRate
                : Math.Max(amount, listingRate);
        }

        amount = decimal.Round(Math.Max(0m, amount), 2, MidpointRounding.AwayFromZero);
        return new ShippingQuote(
            amount == 0 ? "Free shipping" : "Standard shipping",
            amount,
            amount == 0 ? "Free" : string.Format(CultureInfo.GetCultureInfo("en-US"), "{0:C} before tax", amount),
            _options.ShippingEstimateMinDays,
            _options.ShippingEstimateMaxDays);
    }

    public ShippingChoice ChoiceFor(decimal subtotal, IEnumerable<StoreListing?> listings)
    {
        ShippingQuote quote = Quote(subtotal, listings);
        return new ShippingChoice(
            quote.Label,
            ToCents(quote.Amount),
            quote.EstimateMinDays,
            quote.EstimateMaxDays);
    }

    private ShippingQuote Free(string label) =>
        new(label, 0, "Free", _options.ShippingEstimateMinDays, _options.ShippingEstimateMaxDays);

    private static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}

public sealed record ShippingQuote(
    string Label,
    decimal Amount,
    string Summary,
    int EstimateMinDays,
    int EstimateMaxDays);
