using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

public sealed class EfInventoryReferenceDataStore(HaulScoutDbContext db, ICurrentUserContext currentUser) : IInventoryReferenceDataStore
{
    public async Task<IReadOnlyList<string?>> ListStorageLocationValuesAsync(CancellationToken cancellationToken) =>
        await db.InventoryItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.StorageLocation)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrgReferenceOverride>> ListOverridesAsync(CancellationToken cancellationToken) =>
        await db.OrgReferenceOverrides
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
}
