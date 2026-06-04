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
        Guid organizationId = currentUser.OrganizationId;

        var currentInventoryStatuses = new[]
        {
            InventoryStatus.Draft,
            InventoryStatus.Researching,
            InventoryStatus.Watchlist,
            InventoryStatus.Purchased,
            InventoryStatus.NeedsCleaning,
            InventoryStatus.NeedsTesting,
            InventoryStatus.NeedsPhotos,
            InventoryStatus.ReadyToList,
            InventoryStatus.Listed,
            InventoryStatus.Packed
        };

        var unlistedStatuses = new[]
        {
            InventoryStatus.Purchased,
            InventoryStatus.NeedsCleaning,
            InventoryStatus.NeedsTesting,
            InventoryStatus.NeedsPhotos,
            InventoryStatus.ReadyToList
        };

        var currentInventoryQuery = db.InventoryItems
            .Where(item => item.OrganizationId == organizationId && !item.IsArchived && currentInventoryStatuses.Contains(item.Status));

        var staleCutoff = DateTimeOffset.UtcNow.AddDays(-60);

        // EF Core DbContext is NOT thread-safe — sequential queries, but all are SQL projections (fast)
        int currentInventoryCount = await currentInventoryQuery.CountAsync(cancellationToken);
        int activeListingsCount = await db.Listings.CountAsync(item => item.OrganizationId == organizationId && item.Status == ListingStatus.Active, cancellationToken);
        decimal totalInventoryCost = await currentInventoryQuery.SumAsync(item => (decimal?)(item.PurchasePrice ?? 0) ?? 0, cancellationToken);
        decimal estimatedInventoryValue = await currentInventoryQuery.SumAsync(item => (decimal?)(item.EstimatedSalePrice ?? 0) ?? 0, cancellationToken);
        decimal grossSales = await db.Sales.Where(item => item.OrganizationId == organizationId).SumAsync(item => (decimal?)item.SoldPrice ?? 0, cancellationToken);
        decimal netProfit = await db.Sales.Where(item => item.OrganizationId == organizationId).SumAsync(item => (decimal?)item.NetProfit ?? 0, cancellationToken);
        decimal cashDeployed = await currentInventoryQuery.SumAsync(item => (decimal?)(item.PurchasePrice ?? item.SellerAskingPrice ?? 0) ?? 0, cancellationToken);
        int unlistedCount = await currentInventoryQuery.CountAsync(item => unlistedStatuses.Contains(item.Status), cancellationToken);
        int staleCount = await currentInventoryQuery.CountAsync(item => item.CreatedAt <= staleCutoff && item.Status != InventoryStatus.Listed, cancellationToken);

        return new DashboardSummaryDto(
            currentInventoryCount,
            activeListingsCount,
            totalInventoryCost,
            estimatedInventoryValue,
            grossSales,
            netProfit,
            cashDeployed,
            unlistedCount,
            staleCount);
    }
}
