using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

public sealed class EfInventoryIssueStore(HaulScoutDbContext db) : IInventoryIssueStore
{
    /// <summary>Unresolved first, then the operator's chosen order, then newest — the order the list is read in.</summary>
    public async Task<IReadOnlyList<InventoryIssue>> ListAsync(
        Guid organizationId,
        Guid inventoryItemId,
        CancellationToken cancellationToken) =>
        await db.InventoryIssues
            .Where(item => item.InventoryItemId == inventoryItemId && item.OrganizationId == organizationId)
            .OrderBy(item => item.IsResolved)
            .ThenBy(item => item.SortOrder)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<InventoryIssue?> FindAsync(
        Guid organizationId,
        Guid inventoryItemId,
        Guid issueId,
        CancellationToken cancellationToken) =>
        db.InventoryIssues.SingleOrDefaultAsync(
            item => item.Id == issueId && item.InventoryItemId == inventoryItemId && item.OrganizationId == organizationId,
            cancellationToken);

    public Task<bool> PhotoBelongsToItemAsync(
        Guid organizationId,
        Guid inventoryItemId,
        Guid photoId,
        CancellationToken cancellationToken) =>
        db.InventoryPhotos.AnyAsync(
            item => item.Id == photoId && item.InventoryItemId == inventoryItemId && item.OrganizationId == organizationId,
            cancellationToken);

    public async Task<int> NextSortOrderAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken)
    {
        int? max = await db.InventoryIssues
            .Where(item => item.InventoryItemId == inventoryItemId && item.OrganizationId == organizationId)
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken);
        return (max ?? -1) + 1;
    }

    public async Task AddAsync(InventoryIssue issue, CancellationToken cancellationToken) =>
        await db.InventoryIssues.AddAsync(issue, cancellationToken);

    public void Remove(InventoryIssue issue) => db.InventoryIssues.Remove(issue);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
