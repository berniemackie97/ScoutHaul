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
    StorefrontShippingCalculator shipping,
    IOptions<StorefrontCheckoutOptions> options)
{
    private readonly StorefrontCheckoutOptions _options = options.Value;

    public async Task<StorefrontCartDto> GetAsync(Guid organizationId, string? cartToken, CancellationToken cancellationToken)
    {
        Cart? cart = cartToken is null ? null : await carts.FindActiveAsync(organizationId, cartToken, cancellationToken);
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
            cart.Lines.Add(new CartLine
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
                : await carts.FindActiveAsync(organizationId, cart.AnonymousId, cancellationToken);
            if (current is null)
            {
                return AppResult<StorefrontCartDto>.Conflict("Your cart changed while updating. Refresh and try again.");
            }

            return AppResult<StorefrontCartDto>.Ok(BuildDto(current.AnonymousId!, current, []));
        }

        Cart reloaded = await carts.FindActiveAsync(organizationId, cart.AnonymousId!, cancellationToken) ?? cart;
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

        HashSet<Guid> heldByThisCart = await PendingListingIdsAsync(organizationId, cart, cancellationToken);
        return AppResult<StorefrontCartDto>.Ok(BuildDto(cart.AnonymousId!, cart, heldByThisCart));
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
                    line.StoreListing?.ListPrice ?? line.UnitPriceSnapshot,
                    HeroPhotoUrl(line.StoreListing),
                    StorefrontPurchasability.IsPurchasable(line.StoreListing) || heldByThisCart.Contains(line.StoreListingId)))
                .ToArray();

        // Unavailable lines are shown but never counted — the shopper removes them; totals stay honest.
        decimal subtotal = lines.Where(line => line.IsAvailable).Sum(line => line.UnitPrice);
        ShippingQuote shippingQuote = shipping.Quote(
            subtotal,
            cart?.Lines.Where(line => StorefrontPurchasability.IsPurchasable(line.StoreListing)
                    || heldByThisCart.Contains(line.StoreListingId))
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
}
