using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/session")]
public sealed class SessionController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("bootstrap")]
    public async Task<ActionResult<SessionBootstrapDto>> Bootstrap(CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(item => item.Id == currentUser.UserId, cancellationToken);
        var organizations = await db.OrganizationMembers
            .Include(item => item.Organization)
            .Where(item => item.UserId == currentUser.UserId)
            .OrderBy(item => item.Organization!.Name)
            .Select(item => item.Organization!.ToDto())
            .ToListAsync(cancellationToken);

        Guid organizationId = currentUser.OrganizationId;
        var counts = new SessionRemoteCountsDto(
            InventoryItems: await db.InventoryItems.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken),
            Budgets: await db.Budgets.CountAsync(item => item.OrganizationId == organizationId, cancellationToken),
            MarketObservations: await db.MarketObservations.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken),
            Listings: await db.Listings.CountAsync(item => item.OrganizationId == organizationId, cancellationToken),
            Tasks: await db.Tasks.CountAsync(item => item.OrganizationId == organizationId, cancellationToken),
            PendingSyncOperations: 0);

        return new SessionBootstrapDto(
            new SessionUserDto(user.Id, user.Email, user.DisplayName),
            organizations,
            organizationId,
            counts,
            DateTimeOffset.UtcNow);
    }
}
