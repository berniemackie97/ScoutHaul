using System.Security.Cryptography;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using Microsoft.Extensions.Options;

namespace HaulScout.Application.Storefront;

/// <summary>
/// The guest cart. Adding reserves nothing — one-of-one inventory is only held at checkout — so a line's listing can
/// stop being purchasable while it sits in the cart. The cart re-checks on every read and says so per line instead of
/// failing at the end.
/// </summary>
public sealed class CartWorkflowService(
    IStorefrontCartStore carts,
    IStorefrontCheckoutStore checkout,
    IStorefrontCustomerStore customers,
    IStorefrontOfferStore offers,
    StorefrontShippingCalculator shipping,
    IOptions<StorefrontCheckoutOptions> options,
    TimeProvider timeProvider)
{
    private readonly StorefrontCheckoutOptions _options = options.Value;

    public async Task<StorefrontCartDto> GetAsync(Guid organizationId, string? cartToken, CancellationToken cancellationToken)
    {
        Cart? cart = cartToken is null ? null : await carts.FindActiveReadAsync(organizationId, cartToken, cancellationToken);
        HashSet<Guid> heldByThisCart = await PendingListingIdsAsync(organizationId, cart, cancellationToken);
        return BuildDto(cartToken ?? string.Empty, cart, heldByThisCart);
    }

    public async Task<AppResult<StorefrontCartDto>> AddLineAsync(
        Guid organizationId,
        string? cartToken,
        AddCartLineRequest request,
        CancellationToken cancellationToken)
    {
        StoreListing? listing = await carts.FindListingAsync(organizationId, request.StoreListingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<StorefrontCartDto>.NotFound();
        }
        if (!StorefrontPurchasability.IsPurchasable(listing))
        {
            return AppResult<StorefrontCartDto>.Conflict(StorefrontPurchasability.RefusalReason(listing));
        }

        Cart? cart = cartToken is null ? null : await carts.FindActiveAsync(organizationId, cartToken, cancellationToken);
        if (cart is null)
        {
            // A fresh, unguessable token — the cart's only credential, held in the storefront's httpOnly cookie.
            cart = new Cart
            {
                OrganizationId = organizationId,
                AnonymousId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Status = CartStatus.Active,
            };
            await carts.AddAsync(cart, cancellationToken);
        }

        if (cart.Lines.All(line => line.StoreListingId != listing.Id))
        {
            // One-of-one inventory: a listing appears at most once, quantity 1. The price is snapshotted at add
            // time; checkout charges the listing's CURRENT price and the cart surfaces any drift on read.
            carts.AddLine(cart, new CartLine
            {
                OrganizationId = organizationId,
                CartId = cart.Id,
                StoreListingId = listing.Id,
                InventoryItemId = listing.InventoryItemId,
                Quantity = 1,
                UnitPriceSnapshot = listing.ListPrice,
            });
        }

        try
        {
            await carts.SaveChangesAsync(cancellationToken);
        }
        catch (CartMutationConflictException)
        {
            Cart? current = cart.AnonymousId is null
                ? null
                : await carts.FindActiveReadAsync(organizationId, cart.AnonymousId, cancellationToken);
            if (current is null)
            {
                return AppResult<StorefrontCartDto>.Conflict("Your cart changed while updating. Refresh and try again.");
            }

            return AppResult<StorefrontCartDto>.Ok(BuildDto(current.AnonymousId!, current, []));
        }

        Cart reloaded = await carts.FindActiveReadAsync(organizationId, cart.AnonymousId!, cancellationToken) ?? cart;
        return AppResult<StorefrontCartDto>.Ok(BuildDto(cart.AnonymousId!, reloaded, []));
    }

    public async Task<AppResult<StorefrontCartDto>> AddOfferLineAsync(
        Guid organizationId,
        string? cartToken,
        Guid offerId,
        StorefrontCustomerIdentity identity,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Customer customer = await customers.GetOrCreateAsync(organizationId, identity, now, cancellationToken);
        StorefrontOffer? offer = await offers.FindCustomerOfferAsync(organizationId, customer.Id, offerId, cancellationToken);
        if (offer is null)
        {
            return AppResult<StorefrontCartDto>.NotFound("Offer was not found.");
        }

        decimal? approvedAmount = ApprovedOfferAmount(offer, now);
        if (approvedAmount is null)
        {
            return AppResult<StorefrontCartDto>.Conflict("This offer is not approved for checkout.");
        }
        StoreListing? listing = offer.StoreListing;
        if (!StorefrontPurchasability.IsPurchasable(listing))
        {
            return AppResult<StorefrontCartDto>.Conflict(
                listing is null ? "This item is no longer available." : StorefrontPurchasability.RefusalReason(listing));
        }

        Cart? cart = cartToken is null ? null : await carts.FindActiveAsync(organizationId, cartToken, cancellationToken);
        if (cart is null)
        {
            cart = new Cart
            {
                OrganizationId = organizationId,
                CustomerId = customer.Id,
                Customer = customer,
                AnonymousId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Status = CartStatus.Active,
            };
            await carts.AddAsync(cart, cancellationToken);
        }
        else if (cart.CustomerId is Guid existingCustomerId && existingCustomerId != customer.Id)
        {
            return AppResult<StorefrontCartDto>.Conflict("This cart belongs to a different signed-in customer.");
        }
        else
        {
            cart.CustomerId = customer.Id;
            cart.Customer = customer;
        }

        CartLine? existing = cart.Lines.FirstOrDefault(line => line.StoreListingId == offer.StoreListingId);
        if (existing is null)
        {
            carts.AddLine(cart, new CartLine
            {
                OrganizationId = organizationId,
                CartId = cart.Id,
                StoreListingId = offer.StoreListingId,
                InventoryItemId = offer.InventoryItemId,
                Quantity = 1,
                UnitPriceSnapshot = approvedAmount.Value,
                StorefrontOfferId = offer.Id,
            });
        }
        else
        {
            existing.StorefrontOfferId = offer.Id;
            existing.UnitPriceSnapshot = approvedAmount.Value;
        }

        try
        {
            await carts.SaveChangesAsync(cancellationToken);
        }
        catch (CartMutationConflictException)
        {
            Cart? current = cart.AnonymousId is null
                ? null
                : await carts.FindActiveReadAsync(organizationId, cart.AnonymousId, cancellationToken);
            if (current is null)
            {
                return AppResult<StorefrontCartDto>.Conflict("Your cart changed while updating. Refresh and try again.");
            }

            return AppResult<StorefrontCartDto>.Ok(BuildDto(current.AnonymousId!, current, []));
        }

        Cart reloaded = await carts.FindActiveReadAsync(organizationId, cart.AnonymousId!, cancellationToken) ?? cart;
        return AppResult<StorefrontCartDto>.Ok(BuildDto(cart.AnonymousId!, reloaded, []));
    }

    public async Task<AppResult<StorefrontCartDto>> RemoveLineAsync(
        Guid organizationId,
        string? cartToken,
        Guid storeListingId,
        CancellationToken cancellationToken)
    {
        Cart? cart = cartToken is null ? null : await carts.FindActiveAsync(organizationId, cartToken, cancellationToken);
        if (cart is null)
        {
            return AppResult<StorefrontCartDto>.NotFound();
        }

        if (cart.Lines.FirstOrDefault(line => line.StoreListingId == storeListingId) is CartLine line)
        {
            cart.Lines.Remove(line);
            carts.RemoveLine(line);
            await carts.SaveChangesAsync(cancellationToken);
        }

        Cart reloaded = await carts.FindActiveReadAsync(organizationId, cart.AnonymousId!, cancellationToken) ?? cart;
        HashSet<Guid> heldByThisCart = await PendingListingIdsAsync(organizationId, reloaded, cancellationToken);
        return AppResult<StorefrontCartDto>.Ok(BuildDto(cart.AnonymousId!, reloaded, heldByThisCart));
    }

    private StorefrontCartDto BuildDto(string token, Cart? cart, HashSet<Guid> heldByThisCart)
    {
        IReadOnlyList<StorefrontCartLineDto> lines = cart is null
            ? []
            : cart.Lines
                .OrderBy(line => line.CreatedAt)
                .Select(line => new StorefrontCartLineDto(
                    line.StoreListingId,
                    line.StoreListing?.Slug ?? string.Empty,
                    line.StoreListing?.Title ?? "Item",
                    EffectiveUnitPrice(line),
                    HeroPhotoUrl(line.StoreListing),
                    IsLineAvailable(line, heldByThisCart)))
                .ToArray();

        // Unavailable lines are shown but never counted — the shopper removes them; totals stay honest.
        decimal subtotal = lines.Where(line => line.IsAvailable).Sum(line => line.UnitPrice);
        ShippingQuote shippingQuote = shipping.Quote(
            subtotal,
            cart?.Lines.Where(line => IsLineAvailable(line, heldByThisCart))
                .Select(line => line.StoreListing)
                ?? []);

        return new StorefrontCartDto(
            token,
            lines,
            subtotal,
            shippingQuote.Amount,
            shippingQuote.Summary,
            _options.FreeShippingThreshold,
            _options.FreeShippingThreshold is decimal threshold && subtotal >= threshold);
    }

    /// <summary>Same public photo route the catalog uses; the storefront prefixes the API origin.</summary>
    private static string? HeroPhotoUrl(StoreListing? listing)
    {
        Guid? photoId = listing?.InventoryItem?.Photos
            .OrderBy(photo => photo.SortOrder)
            .Select(photo => (Guid?)photo.Id)
            .FirstOrDefault();
        return photoId is null ? null : $"/api/storefront/photos/{photoId}";
    }

    private async Task<HashSet<Guid>> PendingListingIdsAsync(Guid organizationId, Cart? cart, CancellationToken cancellationToken)
    {
        if (cart is null)
        {
            return [];
        }

        Order? pending = await checkout.FindPendingOrderByCartAsync(organizationId, cart.Id, cancellationToken);
        return pending is null
            ? []
            : pending.Lines
                .Where(line => line.StoreListingId is not null)
                .Select(line => line.StoreListingId!.Value)
                .ToHashSet();
    }

    internal static decimal? ApprovedOfferAmount(StorefrontOffer offer, DateTimeOffset now)
    {
        if (offer.ExpiresAt is DateTimeOffset expiresAt && expiresAt <= now)
        {
            return null;
        }

        return offer.Status switch
        {
            StorefrontOfferStatus.Accepted => offer.ResponseAmount ?? offer.OfferAmount,
            StorefrontOfferStatus.Countered => offer.ResponseAmount,
            _ => null,
        };
    }

    private bool IsLineAvailable(CartLine line, HashSet<Guid> heldByThisCart)
    {
        if (line.StorefrontOfferId is not null
            && ApprovedOfferAmount(line.StorefrontOffer!, timeProvider.GetUtcNow()) is null)
        {
            return false;
        }

        return StorefrontPurchasability.IsPurchasable(line.StoreListing) || heldByThisCart.Contains(line.StoreListingId);
    }

    internal static decimal EffectiveUnitPrice(CartLine line) =>
        line.StorefrontOfferId is not null ? line.UnitPriceSnapshot : line.StoreListing?.ListPrice ?? line.UnitPriceSnapshot;
}
