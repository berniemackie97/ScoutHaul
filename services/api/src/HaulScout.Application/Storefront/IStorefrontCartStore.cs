using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>
/// Guest carts, keyed by the anonymous token the storefront keeps in an httpOnly cookie. A cart is per-organization
/// (one physical store today, but the schema is multi-tenant like everything else).
/// </summary>
public interface IStorefrontCartStore
{
    /// <summary>The active cart for this token loaded for mutation, or null. Converted/abandoned carts
    /// never come back — a spent token gets a fresh cart.</summary>
    Task<Cart?> FindActiveAsync(Guid organizationId, string anonymousToken, CancellationToken cancellationToken);

    /// <summary>A hydrated no-tracking snapshot for public reads and post-mutation responses. This must not reuse
    /// the mutation-tracked aggregate because EF can otherwise return stale navigation state after add/remove.</summary>
    Task<Cart?> FindActiveReadAsync(Guid organizationId, string anonymousToken, CancellationToken cancellationToken);

    Task<Cart?> FindActiveByIdAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken);

    Task AddAsync(Cart cart, CancellationToken cancellationToken);

    void AddLine(Cart cart, CartLine line);

    void RemoveLine(CartLine line);

    /// <summary>The listing with its backing item and photos loaded — the raw material for the purchasability rule.</summary>
    Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The cart changed concurrently or duplicated a one-of-one line. The workflow can reload and converge.</summary>
public sealed class CartMutationConflictException(string message, Exception? inner = null) : Exception(message, inner);
