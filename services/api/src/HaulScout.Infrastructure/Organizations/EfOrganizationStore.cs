using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Organizations;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Organizations;

public sealed class EfOrganizationStore(HaulScoutDbContext db) : IOrganizationStore
{
    public async Task<IReadOnlyList<Organization>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.OrganizationMembers
            .Include(member => member.Organization)
            .Where(member => member.UserId == userId && member.Organization != null)
            .OrderBy(member => member.Organization!.Name)
            .Select(member => member.Organization!)
            .ToListAsync(cancellationToken);

    public async Task<AppUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.FindAsync([userId], cancellationToken);

    public async Task<Organization?> FindAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Organizations.FindAsync([organizationId], cancellationToken);

    public async Task AddAsync(Organization organization, CancellationToken cancellationToken) =>
        await db.Organizations.AddAsync(organization, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
