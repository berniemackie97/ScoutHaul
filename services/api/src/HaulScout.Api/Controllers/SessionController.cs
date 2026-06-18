using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/session")]
public sealed class SessionController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("bootstrap")]
    public async Task<ActionResult<SessionBootstrapDto>> Bootstrap(CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();

        Guid userId = currentUser.UserId;
        // Null when the user is org-less (onboarding). Org-scoped counts then naturally come back 0.
        Guid? organizationId = currentUser.ActiveOrganizationId;

        // EF Core DbContext not thread-safe — sequential queries
        var user = await db.Users.SingleAsync(item => item.Id == userId, cancellationToken);
        var organizations = await db.OrganizationMembers
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

        var counts = new SessionRemoteCountsDto(
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
