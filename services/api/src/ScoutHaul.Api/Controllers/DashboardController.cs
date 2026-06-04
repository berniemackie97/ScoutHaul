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

        var currentInventoryCountTask = currentInventoryQuery.CountAsync(cancellationToken);
        var activeListingsCountTask = db.Listings.CountAsync(item => item.OrganizationId == organizationId && item.Status == ListingStatus.Active, cancellationToken);
        var totalInventoryCostTask = currentInventoryQuery.SumAsync(item => (decimal?)(item.PurchasePrice ?? 0) ?? 0, cancellationToken);
        var estimatedInventoryValueTask = currentInventoryQuery.SumAsync(item => (decimal?)(item.EstimatedSalePrice ?? 0) ?? 0, cancellationToken);
        var grossSalesTask = db.Sales.Where(item => item.OrganizationId == organizationId).SumAsync(item => (decimal?)item.SoldPrice ?? 0, cancellationToken);
        var netProfitTask = db.Sales.Where(item => item.OrganizationId == organizationId).SumAsync(item => (decimal?)item.NetProfit ?? 0, cancellationToken);
        var cashDeployedTask = currentInventoryQuery.SumAsync(item => (decimal?)(item.PurchasePrice ?? item.SellerAskingPrice ?? 0) ?? 0, cancellationToken);
        var unlistedCountTask = currentInventoryQuery.CountAsync(item => unlistedStatuses.Contains(item.Status), cancellationToken);
        var staleCountTask = currentInventoryQuery.CountAsync(item => item.CreatedAt <= staleCutoff && item.Status != InventoryStatus.Listed, cancellationToken);

        await Task.WhenAll(
            currentInventoryCountTask, activeListingsCountTask,
            totalInventoryCostTask, estimatedInventoryValueTask,
            grossSalesTask, netProfitTask, cashDeployedTask,
            unlistedCountTask, staleCountTask);

        return new DashboardSummaryDto(
            await currentInventoryCountTask,
            await activeListingsCountTask,
            await totalInventoryCostTask,
            await estimatedInventoryValueTask,
            await grossSalesTask,
            await netProfitTask,
            await cashDeployedTask,
            await unlistedCountTask,
            await staleCountTask);
    }
}
