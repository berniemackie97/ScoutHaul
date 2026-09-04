using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>
/// The checkout's persistence: reservation, the order it creates, and the finalization the webhook performs.
/// Reservation is the one-of-one contention guard, so its contract is precise — see <see cref="TryReserveAsync"/>.
/// </summary>
public interface IStorefrontCheckoutStore
{
    /// <summary>
    /// Atomically flips every listed id from Active to Reserved — all or none. Returns the ids that could NOT be
    /// reserved (already reserved/sold); empty means the caller now holds all of them. Two concurrent checkouts of
    /// the same one-of-one item serialize here: exactly one wins.
    /// </summary>
    Task<IReadOnlyList<Guid>> TryReserveAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken);

    /// <summary>Puts a reservation back — the losing/expired path. Only touches rows still Reserved.</summary>
    Task ReleaseAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken);

    /// <summary>Marks reserved listings sold. Returns the ids that were NOT still Reserved — non-empty means the
    /// conflict path: something took the item between reservation and payment.</summary>
    Task<IReadOnlyList<Guid>> MarkSoldAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken);

    Task AddOrderAsync(Order order, CancellationToken cancellationToken);

    /// <summary>The still-pending order created by a previous checkout of this same cart, if any — the resume path.</summary>
    Task<Order?> FindPendingOrderByCartAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken);

    /// <summary>The order a processor event refers to, with lines + their inventory items loaded. Looked up by
    /// session id, org-agnostic: the webhook has no store header — the order itself says whose it is.</summary>
    Task<Order?> FindOrderBySessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// The order a processor event refers to, by the id we handed the processor as its client reference.
    ///
    /// The recovery path for an order whose session id was never written: the order is saved, the session is
    /// created at the processor, and only then is the id attached — so a failure in between leaves a live,
    /// payable session that no session-id lookup can find.
    /// </summary>
    Task<Order?> FindOrderForOrphanedSessionAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Confirmation-page read: the paid order's public summary fields, by session id.</summary>
    Task<Order?> FindOrderSummaryBySessionAsync(Guid organizationId, string sessionId, CancellationToken cancellationToken);

    /// <summary>The customer this email already is on this org, or null.</summary>
    Task<Customer?> FindCustomerByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken);

    Task<Customer?> FindCustomerByIdAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken);

    Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken);

    Task AddSaleAsync(Sale sale, CancellationToken cancellationToken);

    Task MarkOffersConvertedAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> storefrontOfferIds,
        DateTimeOffset convertedAt,
        CancellationToken cancellationToken);

    /// <summary>Inventory items backing these listings, for the sold-state transition and COGS snapshots.</summary>
    Task<IReadOnlyList<InventoryItem>> ListItemsForListingsAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken);

    /// <summary>The cart an order was checked out from, for conversion on finalize.</summary>
    Task<Cart?> FindCartByIdAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken);

    /// <summary>The org's public store domain — the storefront's redirect origin.</summary>
    Task<string?> GetStoreDomainAsync(Guid organizationId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
