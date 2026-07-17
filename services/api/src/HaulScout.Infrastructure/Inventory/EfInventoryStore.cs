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

    public async Task<IReadOnlyList<InventoryScalars>> ListScalarsAsync(
        Guid organizationId,
        InventoryFilter filter,
        CancellationToken cancellationToken) =>
        await Filtered(organizationId, filter)
            .Select(item => new InventoryScalars(item.Status, item.PurchasePrice, item.EstimatedSalePrice, item.SellerAskingPrice))
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

        if (filter.Lifecycle.HasValue)
        {
            InventoryStatus[] groupStatuses = InventoryLifecycle.InGroup(filter.Lifecycle.Value).ToArray();
            query = query.Where(item => groupStatuses.Contains(item.Status));
        }

        if (filter.Search is not null)
        {
            string term = filter.Search.ToLower();
            query = query.Where(item =>
                item.Title.ToLower().Contains(term)
                || (item.Brand != null && item.Brand.ToLower().Contains(term))
                || (item.Model != null && item.Model.ToLower().Contains(term))
                || (item.Category != null && item.Category.ToLower().Contains(term))
                || (item.Barcode != null && item.Barcode.ToLower().Contains(term))
                || (item.StorageLocation != null && item.StorageLocation.ToLower().Contains(term)));
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
