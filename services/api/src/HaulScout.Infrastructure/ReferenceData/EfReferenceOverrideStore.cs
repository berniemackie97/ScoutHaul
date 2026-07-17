using Microsoft.EntityFrameworkCore;
using HaulScout.Application.ReferenceData;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.ReferenceData;

public sealed class EfReferenceOverrideStore(HaulScoutDbContext db) : IReferenceOverrideStore
{
    public async Task<IReadOnlyList<OrgReferenceOverride>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrgReferenceOverrides
            .Where(item => item.OrganizationId == organizationId)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Value)
            .ToListAsync(cancellationToken);

    public Task<OrgReferenceOverride?> FindByKeyAsync(
        Guid organizationId,
        string kind,
        string value,
        string? parentValue,
        CancellationToken cancellationToken) =>
        db.OrgReferenceOverrides.SingleOrDefaultAsync(
            item => item.OrganizationId == organizationId
                && item.Kind == kind
                && item.Value == value
                && item.ParentValue == parentValue,
            cancellationToken);

    public Task<OrgReferenceOverride?> FindAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.OrgReferenceOverrides.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == organizationId,
            cancellationToken);

    public async Task AddAsync(OrgReferenceOverride row, CancellationToken cancellationToken) =>
        await db.OrgReferenceOverrides.AddAsync(row, cancellationToken);

    public void Remove(OrgReferenceOverride row) => db.OrgReferenceOverrides.Remove(row);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
