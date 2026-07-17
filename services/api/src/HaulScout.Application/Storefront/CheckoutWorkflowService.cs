using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using Microsoft.Extensions.Options;

namespace HaulScout.Application.Storefront;

/// <summary>
/// Turns a cart into a reservation, a pending order, and a hosted payment page. The order of operations is the
/// contract: reserve first (the contention guard), persist the order second, create the payment session last — so
/// money is only ever requested for items this checkout actually holds. The webhook finishes or unwinds it.
/// </summary>
public sealed class CheckoutWorkflowService(
    IStorefrontCartStore carts,
    IStorefrontCheckoutStore checkout,
    IPaymentGateway payments,
    IOptions<StorefrontCheckoutOptions> options,
    TimeProvider timeProvider)
{
    private readonly StorefrontCheckoutOptions _options = options.Value;

    public async Task<AppResult<StorefrontCheckoutResultDto>> CheckoutAsync(
        Guid organizationId,
        string? cartToken,
        CancellationToken cancellationToken)
    {
        Cart? cart = cartToken is null ? null : await carts.FindActiveAsync(organizationId, cartToken, cancellationToken);
        if (cart is null || cart.Lines.Count == 0)
        {
            return AppResult<StorefrontCheckoutResultDto>.Validation("Your cart is empty.");
        }

        // A line whose listing stopped being purchasable never reaches payment. The shopper is told which ones,
        // removes them, and tries again — silently dropping paid-for-looking items would be worse than the friction.
        string[] unavailable = cart.Lines
            .Where(line => !StorefrontPurchasability.IsPurchasable(line.StoreListing))
            .Select(line => line.StoreListing?.Title ?? "An item")
            .ToArray();

        // The shopper's own earlier checkout may still hold these listings — resume it rather than fight it.
        Order? pending = await checkout.FindPendingOrderByCartAsync(organizationId, cart.Id, cancellationToken);
        if (pending?.StripeCheckoutSessionId is string openSession)
        {
            if (await payments.FindOpenCheckoutUrlAsync(openSession, cancellationToken) is string openUrl)
            {
                return AppResult<StorefrontCheckoutResultDto>.Ok(new StorefrontCheckoutResultDto(openUrl));
            }

            // The session is dead but the expiry webhook hasn't landed yet — unwind it here so the shopper
            // isn't locked out of their own items for the rest of the expiry window.
            await ReleasePendingAsync(pending, cancellationToken);
            unavailable = cart.Lines
                .Where(line => !StorefrontPurchasability.IsPurchasable(line.StoreListing)
                    && !pending.Lines.Any(ordered => ordered.StoreListingId == line.StoreListingId))
                .Select(line => line.StoreListing?.Title ?? "An item")
                .ToArray();
        }

        if (unavailable.Length > 0)
        {
            return AppResult<StorefrontCheckoutResultDto>.Conflict(
                $"No longer available: {string.Join(", ", unavailable)}. Remove them from your cart to continue.");
        }

        Guid[] listingIds = cart.Lines.Select(line => line.StoreListingId).ToArray();
        IReadOnlyList<Guid> lost = await checkout.TryReserveAsync(organizationId, listingIds, cancellationToken);
        if (lost.Count > 0)
        {
            string[] lostTitles = cart.Lines
                .Where(line => lost.Contains(line.StoreListingId))
                .Select(line => line.StoreListing?.Title ?? "An item")
                .ToArray();
            return AppResult<StorefrontCheckoutResultDto>.Conflict(
                $"Someone beat you to: {string.Join(", ", lostTitles)}. Remove them from your cart to continue.");
        }

        try
        {
            return await CreateOrderAndSessionAsync(organizationId, cart, cancellationToken);
        }
        catch
        {
            // The reservation must never outlive a checkout that failed to produce a payment page.
            await checkout.ReleaseAsync(organizationId, listingIds, cancellationToken);
            throw;
        }
    }

    public async Task<AppResult<StorefrontOrderSummaryDto>> GetOrderBySessionAsync(
        Guid organizationId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        Order? order = await checkout.FindOrderSummaryBySessionAsync(organizationId, sessionId, cancellationToken);
        if (order is null)
        {
            return AppResult<StorefrontOrderSummaryDto>.NotFound();
        }

        return AppResult<StorefrontOrderSummaryDto>.Ok(new StorefrontOrderSummaryDto(
            order.OrderNumber,
            order.Status.ToString(),
            order.Subtotal,
            order.ShippingTotal,
            order.TaxTotal,
            order.GrandTotal,
            order.IsLocalPickup,
            order.Email,
            order.Lines
                .Select(line => new StorefrontOrderSummaryLineDto(line.TitleSnapshot, line.UnitPrice, null))
                .ToArray()));
    }

    private async Task<AppResult<StorefrontCheckoutResultDto>> CreateOrderAndSessionAsync(
        Guid organizationId,
        Cart cart,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<InventoryItem> items = await checkout.ListItemsForListingsAsync(
            organizationId, cart.Lines.Select(line => line.StoreListingId).ToArray(), cancellationToken);
        Dictionary<Guid, InventoryItem> itemsById = items.ToDictionary(item => item.Id);

        Order order = new()
        {
            OrganizationId = organizationId,
            Channel = "Storefront",
            OrderNumber = $"STO-{now:yyyyMMddHHmmssfff}",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            SourceCartId = cart.Id,
            CreatedAt = now,
        };

        foreach (CartLine line in cart.Lines)
        {
            // The listing's CURRENT price is charged — an add-time snapshot must not honor a since-corrected price.
            decimal unitPrice = line.StoreListing?.ListPrice ?? line.UnitPriceSnapshot;
            InventoryItem? item = itemsById.GetValueOrDefault(line.InventoryItemId);
            order.Lines.Add(new OrderLine
            {
                OrganizationId = organizationId,
                OrderId = order.Id,
                InventoryItemId = line.InventoryItemId,
                StoreListingId = line.StoreListingId,
                TitleSnapshot = line.StoreListing?.Title ?? item?.Title ?? "Item",
                SkuSnapshot = item?.Barcode,
                UnitPrice = unitPrice,
                Quantity = 1,
                LineTotal = unitPrice,
                // Specific-identification COGS, frozen at the moment of sale.
                UnitCostSnapshot = item?.PurchasePrice,
            });
        }

        order.Subtotal = order.Lines.Sum(line => line.LineTotal);
        order.GrandTotal = order.Subtotal;   // Shipping + tax are decided on the payment page; finalization records them.
        await checkout.AddOrderAsync(order, cancellationToken);
        await checkout.SaveChangesAsync(cancellationToken);

        string publicBaseUrl = _options.PublicBaseUrlOverride
            ?? $"https://{await checkout.GetStoreDomainAsync(organizationId, cancellationToken)}";

        HostedCheckout session = await payments.CreateCheckoutAsync(
            new HostedCheckoutRequest(
                order.Id,
                order.OrderNumber,
                order.Lines
                    .Select(line => new HostedCheckoutLine(
                        line.TitleSnapshot,
                        ToCents(line.UnitPrice),
                        HeroImageUrl(line.StoreListingId, cart)))
                    .ToArray(),
                ShippingFor(order.Subtotal),
                _options.LocalPickupEnabled,
                _options.AutomaticTaxEnabled,
                _options.AllowedShipCountries,
                SuccessUrl: $"{publicBaseUrl}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl: $"{publicBaseUrl}/cart",
                // Stripe's floor is 30 minutes; the reservation lives exactly as long as the session.
                ExpiresAt: now.AddMinutes(Math.Max(30, _options.SessionExpiryMinutes))),
            cancellationToken);

        order.StripeCheckoutSessionId = session.SessionId;
        await checkout.SaveChangesAsync(cancellationToken);

        return AppResult<StorefrontCheckoutResultDto>.Ok(new StorefrontCheckoutResultDto(session.Url));
    }

    /// <summary>Free at/above the threshold, the flat rate below it — the two options never show together.</summary>
    private ShippingChoice ShippingFor(decimal subtotal) =>
        _options.FreeShippingThreshold is decimal threshold && subtotal >= threshold
            ? new ShippingChoice("Free shipping", 0, _options.ShippingEstimateMinDays, _options.ShippingEstimateMaxDays)
            : new ShippingChoice("Standard shipping", ToCents(_options.FlatShippingAmount), _options.ShippingEstimateMinDays, _options.ShippingEstimateMaxDays);

    private string? HeroImageUrl(Guid? storeListingId, Cart cart)
    {
        Guid? photoId = cart.Lines
            .FirstOrDefault(line => line.StoreListingId == storeListingId)?.StoreListing?.InventoryItem?.Photos
            .OrderBy(photo => photo.SortOrder)
            .Select(photo => (Guid?)photo.Id)
            .FirstOrDefault();
        // Absolute URL — this renders on Stripe's page, not ours. Without a public API origin, send no image.
        return photoId is null || _options.PublicApiBaseUrl is null
            ? null
            : $"{_options.PublicApiBaseUrl}/api/storefront/photos/{photoId}";
    }

    private async Task ReleasePendingAsync(Order pending, CancellationToken cancellationToken)
    {
        Guid[] listingIds = pending.Lines
            .Where(line => line.StoreListingId is not null)
            .Select(line => line.StoreListingId!.Value)
            .ToArray();
        await checkout.ReleaseAsync(pending.OrganizationId, listingIds, cancellationToken);
        pending.Status = OrderStatus.Cancelled;
        pending.CancelledAt = timeProvider.GetUtcNow();
        await checkout.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Exactly once, at the processor seam: dollars to integer cents.</summary>
    internal static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
