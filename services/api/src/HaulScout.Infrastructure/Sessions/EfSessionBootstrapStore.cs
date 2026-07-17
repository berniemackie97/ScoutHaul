using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Sessions;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Sessions;

public sealed class EfSessionBootstrapStore(HaulScoutDbContext db) : ISessionBootstrapStore
{
    public Task<AppUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<OrganizationDto>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.OrganizationMembers
            .Include(item => item.Organization)
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.Organization!.Name)
            .Select(item => item.Organization!.ToDto())
            .ToListAsync(cancellationToken);

    public async Task<SessionRemoteCountsDto> GetRemoteCountsAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        int inventoryCount = await db.InventoryItems.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken);
        int budgetsCount = await db.Budgets.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);
        int observationsCount = await db.MarketObservations.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken);
        int listingsCount = await db.Listings.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);
        int tasksCount = await db.Tasks.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);

        return new SessionRemoteCountsDto(
            InventoryItems: inventoryCount,
            Budgets: budgetsCount,
            MarketObservations: observationsCount,
            Listings: listingsCount,
            Tasks: tasksCount,
            PendingSyncOperations: 0);
    }
}
