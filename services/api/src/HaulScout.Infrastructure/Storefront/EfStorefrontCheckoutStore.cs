using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

/// <summary>
/// Checkout persistence, including the one-of-one contention guard. Every query ignores the tenant filter and
/// carries an explicit organization predicate instead: the Stripe webhook calls into this store with NO tenant
/// resolved (there is no store header on a webhook), and an unset tenant makes the global filter match nothing.
/// Explicit predicates behave identically in both contexts; the filter does not.
/// </summary>
public sealed class EfStorefrontCheckoutStore(HaulScoutDbContext db) : IStorefrontCheckoutStore
{
    /// <summary>
    /// The guarded bulk update from the design doc (§4): flip Active→Reserved in one statement whose WHERE carries
    /// the expected state — concurrent checkouts of the same item serialize on the row lock, and the loser's
    /// predicate no longer matches. The backing item must also still be on the shelf, so an item sold on another
    /// channel an hour ago cannot be reserved even while its listing wrongly says Active.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> TryReserveAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> storeListingIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        int reserved = await ReservableListings(organizationId, storeListingIds)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(listing => listing.Status, StoreListingStatus.Reserved)
                    .SetProperty(listing => listing.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (reserved == storeListingIds.Count)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        // All or none: a partial hold would strand the winner's items while this checkout can never complete.
        await transaction.RollbackAsync(cancellationToken);
        List<Guid> stillAvailable = await ReservableListings(organizationId, storeListingIds)
            .Select(listing => listing.Id)
            .ToListAsync(cancellationToken);
        return storeListingIds.Except(stillAvailable).ToArray();
    }

    public async Task ReleaseAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> storeListingIds,
        CancellationToken cancellationToken) =>
        await db.StoreListings
            .IgnoreQueryFilters()
            .Where(listing => storeListingIds.Contains(listing.Id)
                && listing.OrganizationId == organizationId
                && listing.Status == StoreListingStatus.Reserved)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(listing => listing.Status, StoreListingStatus.Active)
                    .SetProperty(listing => listing.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);

    public async Task<IReadOnlyList<Guid>> MarkSoldAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> storeListingIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        int sold = await db.StoreListings
            .IgnoreQueryFilters()
            .Where(listing => storeListingIds.Contains(listing.Id)
                && listing.OrganizationId == organizationId
                && listing.Status == StoreListingStatus.Reserved)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(listing => listing.Status, StoreListingStatus.Sold)
                    .SetProperty(listing => listing.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (sold == storeListingIds.Count)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        await transaction.RollbackAsync(cancellationToken);
        List<Guid> stillReserved = await db.StoreListings
            .IgnoreQueryFilters()
            .Where(listing => storeListingIds.Contains(listing.Id)
                && listing.OrganizationId == organizationId
                && listing.Status == StoreListingStatus.Reserved)
            .Select(listing => listing.Id)
            .ToListAsync(cancellationToken);
        return storeListingIds.Except(stillReserved).ToArray();
    }

    public async Task AddOrderAsync(Order order, CancellationToken cancellationToken) =>
        await db.Orders.AddAsync(order, cancellationToken);

    public Task<Order?> FindPendingOrderByCartAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken) =>
        db.Orders
            .IgnoreQueryFilters()
            .Include(order => order.Lines)
            .Where(order => order.SourceCartId == cartId
                && order.OrganizationId == organizationId
                && order.Status == OrderStatus.Pending)
            .OrderByDescending(order => order.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Org-agnostic on purpose: the webhook learns whose order it is FROM the order.</summary>
    public Task<Order?> FindOrderBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
        db.Orders
            .IgnoreQueryFilters()
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.StripeCheckoutSessionId == sessionId, cancellationToken);

    public Task<Order?> FindOrderSummaryBySessionAsync(Guid organizationId, string sessionId, CancellationToken cancellationToken) =>
        db.Orders
            .IgnoreQueryFilters()
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(
                order => order.StripeCheckoutSessionId == sessionId && order.OrganizationId == organizationId,
                cancellationToken);

    public Task<Customer?> FindCustomerByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken) =>
        db.Customers
            .IgnoreQueryFilters()
            .Include(customer => customer.ChannelIdentities)
            .SingleOrDefaultAsync(
                customer => customer.OrganizationId == organizationId && customer.Email != null && customer.Email.ToLower() == email.ToLower(),
                cancellationToken);

    public Task<Customer?> FindCustomerByIdAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken) =>
        db.Customers
            .IgnoreQueryFilters()
            .Include(customer => customer.ChannelIdentities)
            .SingleOrDefaultAsync(
                customer => customer.OrganizationId == organizationId && customer.Id == customerId,
                cancellationToken);

    public async Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken) =>
        await db.Customers.AddAsync(customer, cancellationToken);

    public async Task AddSaleAsync(Sale sale, CancellationToken cancellationToken) =>
        await db.Sales.AddAsync(sale, cancellationToken);

    public async Task MarkOffersConvertedAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> storefrontOfferIds,
        DateTimeOffset convertedAt,
        CancellationToken cancellationToken)
    {
        if (storefrontOfferIds.Count == 0)
        {
            return;
        }

        await db.StorefrontOffers
            .IgnoreQueryFilters()
            .Where(offer => storefrontOfferIds.Contains(offer.Id)
                && offer.OrganizationId == organizationId
                && (offer.Status == StorefrontOfferStatus.Accepted || offer.Status == StorefrontOfferStatus.Countered))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(offer => offer.Status, StorefrontOfferStatus.Converted)
                    .SetProperty(offer => offer.ConvertedAt, convertedAt)
                    .SetProperty(offer => offer.UpdatedAt, convertedAt),
                cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListItemsForListingsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> storeListingIds,
        CancellationToken cancellationToken) =>
        await db.StoreListings
            .IgnoreQueryFilters()
            .Where(listing => storeListingIds.Contains(listing.Id) && listing.OrganizationId == organizationId)
            .Include(listing => listing.InventoryItem!)
                .ThenInclude(item => item.BundleComponents)
                    .ThenInclude(component => component.ComponentInventoryItem)
            .Select(listing => listing.InventoryItem!)
            .ToListAsync(cancellationToken);

    public Task<Cart?> FindCartByIdAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken) =>
        db.Carts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cart => cart.Id == cartId && cart.OrganizationId == organizationId, cancellationToken);

    public Task<string?> GetStoreDomainAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.StoreDomain)
            .FirstOrDefaultAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <summary>Reservable = listing Active AND its item genuinely on the shelf — the cross-channel guard in SQL.</summary>
    private IQueryable<StoreListing> ReservableListings(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds) =>
        db.StoreListings
            .IgnoreQueryFilters()
            .Where(listing => storeListingIds.Contains(listing.Id)
                && listing.OrganizationId == organizationId
                && listing.Status == StoreListingStatus.Active
                && listing.InventoryItem != null
                && !listing.InventoryItem.IsArchived
                && listing.InventoryItem.Status == InventoryStatus.Listed);
}
