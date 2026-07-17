using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

/// <summary>
/// Cart persistence for the public storefront. Runs in anonymous HTTP requests where the store resolver has set the
/// tenant, but every query still carries its explicit organization predicate — the same defense-in-depth as every
/// other module store.
/// </summary>
public sealed class EfStorefrontCartStore(HaulScoutDbContext db) : IStorefrontCartStore
{
    public Task<Cart?> FindActiveAsync(Guid organizationId, string anonymousToken, CancellationToken cancellationToken) =>
        db.Carts
            .Include(cart => cart.Lines)
                .ThenInclude(line => line.StoreListing)
                    .ThenInclude(listing => listing!.InventoryItem)
                        .ThenInclude(item => item!.Photos)
            .SingleOrDefaultAsync(
                cart => cart.AnonymousId == anonymousToken
                    && cart.OrganizationId == organizationId
                    && cart.Status == CartStatus.Active,
                cancellationToken);

    public Task<Cart?> FindActiveByIdAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken) =>
        db.Carts
            .Include(cart => cart.Lines)
            .SingleOrDefaultAsync(
                cart => cart.Id == cartId && cart.OrganizationId == organizationId && cart.Status == CartStatus.Active,
                cancellationToken);

    public async Task AddAsync(Cart cart, CancellationToken cancellationToken) =>
        await db.Carts.AddAsync(cart, cancellationToken);

    public void RemoveLine(CartLine line) => db.CartLines.Remove(line);

    /// <summary>The listing with its item + photos loaded; StorefrontPurchasability decides what it means.</summary>
    public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken) =>
        db.StoreListings
            .Include(listing => listing.InventoryItem)
                .ThenInclude(item => item!.Photos)
            .SingleOrDefaultAsync(
                listing => listing.Id == storeListingId && listing.OrganizationId == organizationId,
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
