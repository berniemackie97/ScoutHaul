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
        await currentUser.ResolveAsync();

        Guid userId = currentUser.UserId;
        Guid organizationId = currentUser.OrganizationId;

        var userTask = db.Users.SingleAsync(item => item.Id == userId, cancellationToken);
        var organizationsTask = db.OrganizationMembers
            .Include(item => item.Organization)
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.Organization!.Name)
            .Select(item => item.Organization!.ToDto())
            .ToListAsync(cancellationToken);

        var inventoryCountTask = db.InventoryItems.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken);
        var budgetsCountTask = db.Budgets.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);
        var observationsCountTask = db.MarketObservations.CountAsync(item => item.OrganizationId == organizationId && !item.IsArchived, cancellationToken);
        var listingsCountTask = db.Listings.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);
        var tasksCountTask = db.Tasks.CountAsync(item => item.OrganizationId == organizationId, cancellationToken);

        await Task.WhenAll(userTask, organizationsTask, inventoryCountTask, budgetsCountTask, observationsCountTask, listingsCountTask, tasksCountTask);

        var user = await userTask;
        var organizations = await organizationsTask;

        var counts = new SessionRemoteCountsDto(
            InventoryItems: await inventoryCountTask,
            Budgets: await budgetsCountTask,
            MarketObservations: await observationsCountTask,
            Listings: await listingsCountTask,
            Tasks: await tasksCountTask,
            PendingSyncOperations: 0);

        return new SessionBootstrapDto(
            new SessionUserDto(user.Id, user.Email, user.DisplayName),
            organizations,
            organizationId,
            counts,
            DateTimeOffset.UtcNow);
    }
}
