using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

public sealed class EfStoreSettingsStore(HaulScoutDbContext db) : IStoreSettingsStore
{
    public Task<Organization?> FindAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.FirstOrDefaultAsync(item => item.Id == organizationId, cancellationToken);

    public Task<bool> StoreDomainExistsForAnotherOrganizationAsync(
        string storeDomain,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        db.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(item => item.StoreDomain == storeDomain && item.Id != organizationId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
