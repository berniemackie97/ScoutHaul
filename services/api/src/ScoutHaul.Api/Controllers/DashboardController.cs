using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[RequireOrganizationPermission(ScoutHaulPermissions.ViewReports)]
public sealed class DashboardController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken cancellationToken)
    {
        var inventory = await db.InventoryItems
            .Where(item => item.OrganizationId == currentUser.OrganizationId && !item.IsArchived)
            .ToListAsync(cancellationToken);

        var listings = await db.Listings
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);

        var sales = await db.Sales
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);

        var currentInventory = inventory.Where(item => InventoryStatusRules.IsCurrentInventory(item.Status)).ToList();

        var staleCutoff = DateTimeOffset.UtcNow.AddDays(-60);
        var staleInventoryCount = currentInventory.Count(item => item.CreatedAt <= staleCutoff && item.Status != InventoryStatus.Listed);

        return new DashboardSummaryDto(
            currentInventory.Count,
            listings.Count(item => item.Status == ListingStatus.Active),
            currentInventory.Sum(item => item.PurchasePrice ?? 0),
            currentInventory.Sum(item => item.EstimatedSalePrice ?? 0),
            sales.Sum(item => item.SoldPrice),
            sales.Sum(item => item.NetProfit),
            currentInventory.Sum(item => item.PurchasePrice ?? item.SellerAskingPrice ?? 0),
            currentInventory.Count(item => InventoryStatusRules.IsUnlistedInventory(item.Status)),
            staleInventoryCount);
    }
}
