using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Builds the payload a client needs on sign-in: who the user is, the organizations they belong to, the active one,
/// and the org-scoped record counts a mobile client reconciles its local store against.
/// </summary>
public sealed class SessionBootstrapService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<SessionBootstrapDto> BuildAsync(CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();

        Guid userId = currentUser.UserId;
        // Null when the user is org-less (onboarding). Org-scoped counts then naturally come back 0.
        Guid? organizationId = currentUser.ActiveOrganizationId;

        // EF Core DbContext not thread-safe — sequential queries
        AppUser user = await db.Users.SingleAsync(item => item.Id == userId, cancellationToken);
        List<OrganizationDto> organizations = await db.OrganizationMembers
            .Include(item => item.Organization)
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.Organization!.Name)
            .Select(item => item.Organization!.ToDto())
            .ToListAsync(cancellationToken);

        int inventoryCount = await db.InventoryItems.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken);
        int budgetsCount = await db.Budgets.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);
        int observationsCount = await db.MarketObservations.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken);
        int listingsCount = await db.Listings.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);
        int tasksCount = await db.Tasks.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);

        SessionRemoteCountsDto counts = new(
            InventoryItems: inventoryCount,
            Budgets: budgetsCount,
            MarketObservations: observationsCount,
            Listings: listingsCount,
            Tasks: tasksCount,
            PendingSyncOperations: 0);

        return new SessionBootstrapDto(
            new SessionUserDto(user.Id, user.Email, user.DisplayName),
            organizations,
            organizationId,
            counts,
            DateTimeOffset.UtcNow);
    }
}
