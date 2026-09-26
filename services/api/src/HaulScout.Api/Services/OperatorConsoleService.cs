using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Channels;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// What the operator should look at first: work waiting, stock on hand, how trading is going.
///
/// It computes nothing of its own. Every figure is borrowed from the service that owns it — order
/// stages from <see cref="OrdersWorkflowService"/>, inventory counts through the browse list's own
/// filter, money from <see cref="BusinessReportService"/> — because the home page it replaces kept
/// private copies of those rules, and they had drifted: its "Not listed" skipped items awaiting
/// repair, its "Aged" counted by a different date than the list it linked to, and its net profit
/// was a separate SQL sum from the one the Reports page shows.
/// </summary>
public sealed class OperatorConsoleService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    IOrganizationAuthorizer access,
    OrdersWorkflowService orders,
    InventoryQueryService inventory,
    IStorefrontOfferStore offers,
    IPlatformOrderStore platformOrders,
    ChannelDelistService delisting,
    SoldListingService soldListings,
    BusinessReportService reports,
    TimeProvider clock)
{
    /// <summary>The trading window the console reports on, compared with the same length before it.</summary>
    public const int MoneyWindowDays = 30;

    public const string Attention = "attention";
    public const string Work = "work";
    public const string Hygiene = "hygiene";

    /// <summary>A penny of rounding is not "unallocated cost".</summary>
    private const decimal AllocationTolerance = 0.01m;

    public async Task<OperatorConsoleDto> BuildAsync(CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        bool canOrders = await access.HasPermission(organizationId, HaulScoutPermissions.ManageOrders, cancellationToken);
        bool canInventory = await access.HasPermission(organizationId, HaulScoutPermissions.ManageInventory, cancellationToken);
        bool canFinance = await access.HasPermission(organizationId, HaulScoutPermissions.ManageFinance, cancellationToken);
        bool canReports = await access.HasPermission(organizationId, HaulScoutPermissions.ViewReports, cancellationToken);

        List<ConsoleQueueDto> queues = [];

        if (canOrders)
        {
            OrderQueueSummaryDto summary = await orders.QueueSummaryAsync(cancellationToken);
            int Stage(OrderWorkflowStage stage) =>
                summary.Stages.SingleOrDefault(entry => entry.Stage == stage.ToString())?.Count ?? 0;

            queues.Add(new("orders.needsAttention", Attention, Stage(OrderWorkflowStage.NeedsAttention)));
            IReadOnlyDictionary<StorefrontOfferStatus, int> offerCounts = await offers.CountByStatusAsync(organizationId, cancellationToken);
            queues.Add(new("offers.new", Attention, offerCounts.GetValueOrDefault(StorefrontOfferStatus.New)));
            queues.Add(new("orders.toPack", Work, Stage(OrderWorkflowStage.ToPack)));
            queues.Add(new("orders.toShip", Work, Stage(OrderWorkflowStage.ToShip)));
            queues.Add(new("orders.readyForPickup", Work, Stage(OrderWorkflowStage.ReadyForPickup)));
            queues.Add(new("orders.awaitingPayment", Work, Stage(OrderWorkflowStage.AwaitingPayment)));
        }

        if (canFinance)
        {
            // Imported but not turned into sales: the item still reads as available while it is in a
            // buyer's hands, which is how the same thing gets sold twice.
            queues.Add(new("imports.unreconciled", Attention,
                await platformOrders.CountAsync(organizationId, PlatformOrderStatus.Imported, cancellationToken)));

            (int lots, decimal amount) = await UnallocatedPurchasesAsync(organizationId, cancellationToken);
            queues.Add(new("purchases.unallocated", Hygiene, lots, amount));
        }

        ConsoleInventoryDto? stock = null;
        if (canInventory)
        {
            // Each filter is exactly the one the destination list applies for the link the portal
            // builds from the key. That is the whole contract of this method.
            // Sold here, still for sale there: the one queue where waiting costs a sale that has to be refunded.
            queues.Add(new("listings.pendingDelist", Attention, await delisting.CountAsync(cancellationToken)));
            // A marketplace says it sold and the books do not know: missing money, and stock that is not there.
            queues.Add(new("listings.soldNotRecorded", Attention, await soldListings.CountAsync(cancellationToken)));
            queues.Add(new("inventory.readyToList", Work, await inventory.CountAsync(new InventoryFilter(Status: InventoryStatus.ReadyToList), cancellationToken)));
            queues.Add(new("inventory.benchWork", Work, await inventory.CountAsync(new InventoryFilter(NeedsBenchWork: true), cancellationToken)));
            queues.Add(new("inventory.missingCost", Hygiene, await inventory.CountAsync(new InventoryFilter(Attention: InventoryAttention.MissingCost), cancellationToken)));
            queues.Add(new("inventory.missingPhotos", Hygiene, await inventory.CountAsync(ActiveWith(InventoryAttention.MissingPhotos), cancellationToken)));
            queues.Add(new("inventory.missingLocation", Hygiene, await inventory.CountAsync(ActiveWith(InventoryAttention.MissingLocation), cancellationToken)));
            queues.Add(new("inventory.aged", Hygiene, await inventory.CountAsync(ActiveWith(InventoryAttention.Aged), cancellationToken)));

            InventorySummaryDto active = await inventory.SummaryAsync(new InventoryFilter(Lifecycle: InventoryLifecycleGroup.Active), cancellationToken);
            stock = new ConsoleInventoryDto(active.Total, active.CapitalDeployed, active.EstimatedResaleValue);
        }

        ConsoleMoneyDto? money = null;
        if (canReports)
        {
            DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            ReportRange range = ReportRange.Trailing(today, MoneyWindowDays);
            BusinessReportDto report = await reports.BuildAsync(range, cancellationToken);
            money = new ConsoleMoneyDto(
                MoneyWindowDays,
                DateOnly.FromDateTime(range.From.UtcDateTime),
                range.ToInclusive,
                report.Kpis,
                report.Comparison);
        }

        return new OperatorConsoleDto(queues, stock, money, clock.GetUtcNow());
    }

    /// <summary>
    /// The legacy mobile summary, rebuilt from the same definitions as the console.
    ///
    /// Its shape is unchanged so the app keeps working; its figures change where they were wrong.
    /// "Unlisted" now includes items awaiting repair, "stale" uses the same date as the Aged list,
    /// and cash deployed no longer counts an asking price as money spent.
    /// </summary>
    public async Task<DashboardSummaryDto> LegacySummaryAsync(CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        InventoryFilter activeFilter = new(Lifecycle: InventoryLifecycleGroup.Active);
        InventorySummaryDto active = await inventory.SummaryAsync(activeFilter, cancellationToken);

        int unlisted = await inventory.CountAsync(new InventoryFilter(Attention: InventoryAttention.Unlisted), cancellationToken);
        int stale = await inventory.CountAsync(ActiveWith(InventoryAttention.Aged), cancellationToken);
        int activeListings = await db.Listings.CountAsync(
            listing => listing.OrganizationId == organizationId && listing.Status == ListingStatus.Active,
            cancellationToken);

        var realized = await db.Sales
            .Where(sale => sale.OrganizationId == organizationId && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status))
            .GroupBy(sale => 1)
            .Select(group => new
            {
                Gross = group.Sum(sale => sale.SoldPrice + sale.ShippingCharged),
                Net = group.Sum(sale => sale.NetProfit),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new DashboardSummaryDto(
            active.Total,
            activeListings,
            active.CapitalDeployed,
            active.EstimatedResaleValue,
            realized?.Gross ?? 0,
            realized?.Net ?? 0,
            active.CapitalDeployed,
            unlisted,
            stale);
    }

    /// <summary>
    /// Attention filters narrowed to stock that is still for sale. A delivered item without photos is
    /// not a problem anyone needs to fix, and counting it buries the ones that are.
    /// </summary>
    private static InventoryFilter ActiveWith(InventoryAttention attention) =>
        new(Lifecycle: InventoryLifecycleGroup.Active, Attention: attention);

    /// <summary>
    /// Purchases whose landed cost is not yet fully carried by items — including lots recorded at the
    /// till and never itemized. Until they are, cost of goods on every sale from them is understated.
    /// </summary>
    private async Task<(int Lots, decimal Amount)> UnallocatedPurchasesAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var lots = await db.AcquisitionLots
            .Where(lot => lot.OrganizationId == organizationId)
            .Select(lot => new
            {
                Landed = lot.PurchaseSubtotal + lot.ShippingCost + lot.TaxCost + lot.FeeCost + lot.OtherCost,
                Allocated = lot.Items.Sum(item => (decimal?)item.AllocatedCost) ?? 0,
            })
            .ToListAsync(cancellationToken);

        List<decimal> open = lots
            .Select(lot => lot.Landed - lot.Allocated)
            .Where(gap => gap >= AllocationTolerance)
            .ToList();

        return (open.Count, open.Sum());
    }
}
