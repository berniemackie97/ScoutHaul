using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// The EF side of <see cref="IInventoryStore"/>. All the query composition the use cases used to do inline lives here,
/// which is the point: filtering and sorting must run as SQL, and this is the only layer allowed to know that.
/// </summary>
public sealed class EfInventoryStore(HaulScoutDbContext db) : IInventoryStore
{
    public Task<InventoryItem?> FindDetailAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems
            .Include(item => item.Photos)
            .Include(item => item.Issues)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
                .ThenInclude(item => item.ComponentInventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == organizationId, cancellationToken);

    public Task<InventoryItem?> FindAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems.SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == organizationId, cancellationToken);

    public Task<bool> ExistsAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems.AnyAsync(item => item.Id == id && item.OrganizationId == organizationId, cancellationToken);

    public Task<bool> HasActiveSaleAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken) =>
        db.Sales.AnyAsync(
            sale => sale.InventoryItemId == inventoryItemId
                && sale.OrganizationId == organizationId
                && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status),
            cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> ListAsync(
        Guid organizationId,
        InventoryFilter filter,
        int take,
        CancellationToken cancellationToken) =>
        await Hydrated(Filtered(organizationId, filter))
            .OrderByDescending(item => item.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> BrowseAsync(
        Guid organizationId,
        InventoryFilter filter,
        InventorySort sort,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await Hydrated(Sorted(Filtered(organizationId, filter), sort))
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(Guid organizationId, InventoryFilter filter, CancellationToken cancellationToken) =>
        Filtered(organizationId, filter).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryScalars>> ListScalarsAsync(
        Guid organizationId,
        InventoryFilter filter,
        CancellationToken cancellationToken) =>
        await Filtered(organizationId, filter)
            .Select(item => new InventoryScalars(
                item.Status,
                item.PurchasePrice,
                item.EstimatedSalePrice,
                item.SellerAskingPrice,
                item.FunctionalStatus,
                item.OwnershipType))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> ListBundleCandidatesAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await db.InventoryItems
            .Include(item => item.Sales)
            .Where(item => ids.Contains(item.Id) && item.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken) =>
        await db.InventoryItems.AddAsync(item, cancellationToken);

    public async Task AddBundleComponentAsync(InventoryBundleComponent component, CancellationToken cancellationToken) =>
        await db.InventoryBundleComponents.AddAsync(component, cancellationToken);

    public Task LoadBundleComponentsAsync(InventoryItem bundle, CancellationToken cancellationToken) =>
        db.Entry(bundle).Collection(item => item.BundleComponents).LoadAsync(cancellationToken);

    public Task<InventoryItem?> FindWithBundleComponentsAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems
            .Include(item => item.BundleComponents)
                .ThenInclude(component => component.ComponentInventoryItem)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == organizationId, cancellationToken);

    public async Task<InventoryReferences> InspectReferencesAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken) =>
        new(
            HasActiveSale: await db.Sales.AnyAsync(
                sale => sale.InventoryItemId == id
                    && sale.OrganizationId == organizationId
                    && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status),
                cancellationToken),
            HasSaleHistory: await db.Sales.AnyAsync(
                sale => sale.InventoryItemId == id && sale.OrganizationId == organizationId,
                cancellationToken),
            IsBundleComponent: await db.InventoryBundleComponents.AnyAsync(
                component => component.ComponentInventoryItemId == id && component.OrganizationId == organizationId,
                cancellationToken),
            HasLiveChannelListing: await db.ChannelListings.AnyAsync(
                listing => listing.InventoryItemId == id
                    && listing.OrganizationId == organizationId
                    && (listing.Status == ChannelListingStatus.Active || listing.Status == ChannelListingStatus.PendingDelist),
                cancellationToken),
            HasActiveMarketplaceListing: await db.Listings.AnyAsync(
                listing => listing.InventoryItemId == id
                    && listing.OrganizationId == organizationId
                    && listing.Status == ListingStatus.Active,
                cancellationToken),
            HasActiveStorefrontListing: await db.StoreListings.AnyAsync(
                listing => listing.InventoryItemId == id
                    && listing.OrganizationId == organizationId
                    && (listing.Status == StoreListingStatus.Active || listing.Status == StoreListingStatus.Reserved),
                cancellationToken),
            HasOrderHistory: await db.OrderLines.AnyAsync(
                line => line.InventoryItemId == id && line.OrganizationId == organizationId,
                cancellationToken));

    /// <summary>
    /// Channel listings and expenses exist only for this item, so they go with it. Market observations are research
    /// that stands on its own — they are detached rather than destroyed, so the pricing history survives the item.
    /// </summary>
    public async Task RemoveWithDependentsAsync(InventoryItem item, CancellationToken cancellationToken)
    {
        Guid organizationId = item.OrganizationId;

        List<ChannelListing> channelListings = await db.ChannelListings
            .Where(listing => listing.InventoryItemId == item.Id && listing.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        db.ChannelListings.RemoveRange(channelListings);

        List<Expense> expenses = await db.Expenses
            .Where(expense => expense.InventoryItemId == item.Id && expense.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        db.Expenses.RemoveRange(expenses);

        List<MarketObservation> observations = await db.MarketObservations
            .Where(observation => observation.InventoryItemId == item.Id && observation.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        foreach (MarketObservation observation in observations)
        {
            observation.InventoryItemId = null;
        }

        db.InventoryItems.Remove(item);
    }

    public async Task<bool> SourcingReferencesValidAsync(
        Guid organizationId,
        Guid? sourcingTripId,
        Guid? sourcingTripStopId,
        CancellationToken cancellationToken)
    {
        if (sourcingTripStopId.HasValue)
        {
            var stop = await db.SourcingTripStops
                .Where(item => item.Id == sourcingTripStopId.Value && item.OrganizationId == organizationId)
                .Select(item => new { item.SourcingTripId })
                .SingleOrDefaultAsync(cancellationToken);

            if (stop is null)
            {
                return false;
            }

            // A stop belonging to a different trip would misattribute the item's cost to the wrong sourcing run.
            if (sourcingTripId.HasValue && stop.SourcingTripId != sourcingTripId.Value)
            {
                return false;
            }
        }

        if (sourcingTripId.HasValue
            && !await db.SourcingTrips.AnyAsync(
                item => item.Id == sourcingTripId.Value && item.OrganizationId == organizationId,
                cancellationToken))
        {
            return false;
        }

        return true;
    }

    public Task<Expense?> FindPurchaseExpenseAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken) =>
        db.Expenses.SingleOrDefaultAsync(
            expense => expense.OrganizationId == organizationId
                && expense.InventoryItemId == inventoryItemId
                && expense.Category == ExpenseCategory.InventoryPurchase,
            cancellationToken);

    public async Task AddExpenseAsync(Expense expense, CancellationToken cancellationToken) =>
        await db.Expenses.AddAsync(expense, cancellationToken);

    public void RemoveExpense(Expense expense) => db.Expenses.Remove(expense);

    public Task<Budget?> FindActiveBudgetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Budgets
            .Where(item => item.OrganizationId == organizationId && item.IsActive)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListSalesWithRefundsAsync(
        Guid organizationId,
        Guid inventoryItemId,
        CancellationToken cancellationToken) =>
        await db.Sales
            .Include(sale => sale.Refunds)
            .Where(sale => sale.InventoryItemId == inventoryItemId && sale.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

    public async Task<int> NextSaleCorrectionVersionAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken) =>
        await db.SaleCorrections.CountAsync(
            correction => correction.SaleId == saleId && correction.OrganizationId == organizationId,
            cancellationToken) + 1;

    public async Task AddSaleCorrectionAsync(SaleCorrection correction, CancellationToken cancellationToken) =>
        await db.SaleCorrections.AddAsync(correction, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private IQueryable<InventoryItem> Filtered(Guid organizationId, InventoryFilter filter)
    {
        IQueryable<InventoryItem> query = db.InventoryItems.Where(item => item.OrganizationId == organizationId);

        if (!filter.IncludeArchived)
        {
            query = query.Where(item => !item.IsArchived);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(item => item.Status == filter.Status.Value);
        }

        if (filter.Attention.HasValue)
        {
            switch (filter.Attention.Value)
            {
                case InventoryAttention.Unlisted:
                    // Reuse the domain rule so this can never drift from the counts the action cards show.
                    InventoryStatus[] unlisted = Enum.GetValues<InventoryStatus>().Where(InventoryStatusRules.IsUnlistedInventory).ToArray();
                    query = query.Where(item => unlisted.Contains(item.Status));
                    break;
                case InventoryAttention.Aged:
                    DateTimeOffset agedCutoff = DateTimeOffset.UtcNow.AddDays(-60);
                    query = query.Where(item => item.PurchasedAt < agedCutoff && item.Status != InventoryStatus.Listed);
                    break;
                case InventoryAttention.MissingPhotos:
                    query = query.Where(item => !item.Photos.Any());
                    break;
                case InventoryAttention.MissingCost:
                    query = query.Where(item => item.PurchasePrice == null);
                    break;
                case InventoryAttention.MissingValue:
                    query = query.Where(item => item.EstimatedSalePrice == null);
                    break;
                case InventoryAttention.MissingLocation:
                    query = query.Where(item => item.StorageLocation == null || item.StorageLocation == "");
                    break;
            }
        }

        if (filter.Lifecycle.HasValue)
        {
            InventoryStatus[] groupStatuses = InventoryLifecycle.InGroup(filter.Lifecycle.Value).ToArray();
            query = query.Where(item => groupStatuses.Contains(item.Status));
        }

        if (filter.Ownership.HasValue)
        {
            query = query.Where(item => item.OwnershipType == filter.Ownership.Value);
        }

        if (filter.NeedsBenchWork)
        {
            // The domain owns what "needs bench work" means; this only translates it into SQL, so the
            // queue and its count cannot disagree with each other or with anything else that asks.
            InventoryFunctionalStatus[] blocking = InventoryBenchWork.BlockingStates;
            query = query.Where(item =>
                item.OwnershipType == InventoryOwnershipType.CustomerRepair
                || blocking.Contains(item.FunctionalStatus));
        }

        if (filter.LotCandidates)
        {
            query = query.Where(item => !item.AcquisitionLotItems.Any() && item.Status != InventoryStatus.Bundled);
        }

        if (filter.Search is not null)
        {
            query = query.Where(InventorySearch.Matching(filter.Search));
        }

        return query;
    }

    private static IQueryable<InventoryItem> Sorted(IQueryable<InventoryItem> query, InventorySort sort) =>
        (sort.Field, sort.Descending) switch
        {
            (InventorySortField.Title, false) => query.OrderBy(item => item.Title),
            (InventorySortField.Title, true) => query.OrderByDescending(item => item.Title),
            (InventorySortField.PurchasePrice, false) => query.OrderBy(item => item.PurchasePrice),
            (InventorySortField.PurchasePrice, true) => query.OrderByDescending(item => item.PurchasePrice),
            (InventorySortField.EstimatedSalePrice, false) => query.OrderBy(item => item.EstimatedSalePrice),
            (InventorySortField.EstimatedSalePrice, true) => query.OrderByDescending(item => item.EstimatedSalePrice),
            (InventorySortField.CreatedAt, false) => query.OrderBy(item => item.CreatedAt),
            (InventorySortField.CreatedAt, true) => query.OrderByDescending(item => item.CreatedAt),
            (_, false) => query.OrderBy(item => item.UpdatedAt),
            (_, true) => query.OrderByDescending(item => item.UpdatedAt),
        };

    private static IQueryable<InventoryItem> Hydrated(IQueryable<InventoryItem> query) =>
        query
            .Include(item => item.Photos)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents);
}
