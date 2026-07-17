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
public sealed class CartWorkflowService(IStorefrontCartStore carts, IOptions<StorefrontCheckoutOptions> options)
{
    private readonly StorefrontCheckoutOptions _options = options.Value;

    public async Task<StorefrontCartDto> GetAsync(Guid organizationId, string? cartToken, CancellationToken cancellationToken)
    {
        Cart? cart = cartToken is null ? null : await carts.FindActiveAsync(organizationId, cartToken, cancellationToken);
        return BuildDto(cartToken ?? string.Empty, cart);
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
                StoreListing = listing,
            });
        }

        await carts.SaveChangesAsync(cancellationToken);
        return AppResult<StorefrontCartDto>.Ok(BuildDto(cart.AnonymousId!, cart));
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

        return AppResult<StorefrontCartDto>.Ok(BuildDto(cart.AnonymousId!, cart));
    }

    private StorefrontCartDto BuildDto(string token, Cart? cart)
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
                    StorefrontPurchasability.IsPurchasable(line.StoreListing)))
                .ToArray();

        // Unavailable lines are shown but never counted — the shopper removes them; totals stay honest.
        decimal subtotal = lines.Where(line => line.IsAvailable).Sum(line => line.UnitPrice);
        return new StorefrontCartDto(
            token,
            lines,
            subtotal,
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
}
