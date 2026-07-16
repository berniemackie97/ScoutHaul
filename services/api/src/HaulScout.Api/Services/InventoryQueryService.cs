using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

public sealed class InventoryQueryService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<InventoryItemDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        InventoryItem? item = await FindItemAsync(id, cancellationToken);
        return item?.ToDto();
    }

    public async Task<IReadOnlyList<InventoryItemDto>> ListAsync(
        InventoryStatus? status,
        InventoryLifecycleGroup? lifecycle,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        IQueryable<InventoryItem> query = BaseQuery(includeArchived, status, lifecycle);

        return await query
            .Include(item => item.Photos)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryListDto> BrowseAsync(
        InventoryStatus? status,
        InventoryLifecycleGroup? lifecycle,
        bool includeArchived,
        string? search,
        string? sort,
        bool desc,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<InventoryItem> query = BaseQuery(includeArchived, status, lifecycle);

        string? term = TrimToNull(search)?.ToLower();
        if (term is not null)
        {
            query = query.Where(item =>
                item.Title.ToLower().Contains(term)
                || (item.Brand != null && item.Brand.ToLower().Contains(term))
                || (item.Model != null && item.Model.ToLower().Contains(term))
                || (item.Category != null && item.Category.ToLower().Contains(term))
                || (item.Barcode != null && item.Barcode.ToLower().Contains(term))
                || (item.StorageLocation != null && item.StorageLocation.ToLower().Contains(term)));
        }

        var scalars = await query
            .Select(item => new { item.Status, item.PurchasePrice, item.EstimatedSalePrice, item.SellerAskingPrice })
            .ToListAsync(cancellationToken);
        int total = scalars.Count;
        InventorySummaryDto summary = new(
            total,
            scalars.Count(item => InventoryLifecycle.Group(item.Status) == InventoryLifecycleGroup.Active),
            scalars.Count(item => item.Status is InventoryStatus.ReadyToList or InventoryStatus.Listed),
            scalars.Count(item => InventoryLifecycle.IsSold(item.Status)),
            scalars.Sum(item => item.PurchasePrice ?? 0),
            scalars.Sum(item => item.EstimatedSalePrice ?? item.SellerAskingPrice ?? 0));

        query = sort?.ToLowerInvariant() switch
        {
            "title" => desc ? query.OrderByDescending(item => item.Title) : query.OrderBy(item => item.Title),
            "purchaseprice" => desc ? query.OrderByDescending(item => item.PurchasePrice) : query.OrderBy(item => item.PurchasePrice),
            "estimatedsaleprice" => desc ? query.OrderByDescending(item => item.EstimatedSalePrice) : query.OrderBy(item => item.EstimatedSalePrice),
            "createdat" => desc ? query.OrderByDescending(item => item.CreatedAt) : query.OrderBy(item => item.CreatedAt),
            _ => desc ? query.OrderByDescending(item => item.UpdatedAt) : query.OrderBy(item => item.UpdatedAt),
        };

        List<InventoryItem> pageItems = await query
            .Include(item => item.Photos)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        List<InventoryItemDto> dtos = pageItems.Select(item => item.ToDto()).ToList();
        int pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        return new InventoryListDto(dtos, total, page, pageSize, pageCount, summary);
    }

    private IQueryable<InventoryItem> BaseQuery(
        bool includeArchived,
        InventoryStatus? status,
        InventoryLifecycleGroup? lifecycle)
    {
        IQueryable<InventoryItem> query = db.InventoryItems.Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (!includeArchived)
        {
            query = query.Where(item => !item.IsArchived);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        if (lifecycle.HasValue)
        {
            InventoryStatus[] groupStatuses = InventoryLifecycle.InGroup(lifecycle.Value).ToArray();
            query = query.Where(item => groupStatuses.Contains(item.Status));
        }

        return query;
    }

    private Task<InventoryItem?> FindItemAsync(Guid id, CancellationToken cancellationToken) =>
        db.InventoryItems
            .Include(item => item.Photos)
            .Include(item => item.Issues)
            .Include(item => item.AcquisitionLotItems)
                .ThenInclude(item => item.AcquisitionLot)
            .Include(item => item.BundleComponents)
                .ThenInclude(item => item.ComponentInventoryItem)
            .SingleOrDefaultAsync(
                item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
}
