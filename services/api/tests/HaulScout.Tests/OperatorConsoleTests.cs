using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Storefront;

using HaulScout.Application.Channels;

using HaulScout.Infrastructure.Channels;

namespace HaulScout.Tests;

/// <summary>
/// The home console reports what is waiting, and every figure agrees with the page it opens.
///
/// The page it replaces kept private copies of the rules behind its cards, and they had drifted from
/// the lists those cards linked to: "Not listed" left out items awaiting repair, "Aged" counted by a
/// different date, and "cash deployed" counted asking prices as money spent. So the contract pinned
/// here is not "the numbers look right" but "the numbers are the list's own numbers".
/// </summary>
public sealed class OperatorConsoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);

    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class StubAuthorizer(params string[] granted) : IOrganizationAuthorizer
    {
        public Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            granted.Contains(permission) ? Task.CompletedTask : throw new UnauthorizedAccessException(permission);

        public Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(granted.Contains(permission));

        public Task<IReadOnlyList<string>> ListPermissions(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(granted);

        public Task RequireOwner(Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static readonly string[] Owner =
    [
        HaulScoutPermissions.ManageOrders,
        HaulScoutPermissions.ManageInventory,
        HaulScoutPermissions.ManageListings,
        HaulScoutPermissions.ManageFinance,
        HaulScoutPermissions.ViewReports,
    ];

    private sealed record Harness(OperatorConsoleService Console, InventoryQueryService Inventory, HaulScoutDbContext Db, Guid Org);

    private static Harness NewHarness(params string[] granted)
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();

        StubCurrentUser currentUser = new(org);
        OrderAssembler assembler = new(db, currentUser);
        InventoryQueryService inventory = new(new EfInventoryStore(db, new FakeTimeProvider(Now)), currentUser);
        OperatorConsoleService console = new(
            db,
            currentUser,
            new StubAuthorizer(granted),
            OrdersServiceFactory.Create(db, currentUser, assembler),
            inventory,
            new EfStorefrontOfferStore(db),
            new EfPlatformOrderStore(db, assembler),
            new ChannelDelistService(new EfChannelDelistStore(db), currentUser, TimeProvider.System),
            new SoldListingService(new EfSoldListingStore(db), currentUser, TimeProvider.System),
            new BusinessReportService(db, currentUser),
            new FakeTimeProvider(Now));

        return new Harness(console, inventory, db, org);
    }

    private static InventoryItem Item(
        Guid org,
        InventoryStatus status = InventoryStatus.ReadyToList,
        decimal? cost = 10m,
        string? location = "Shelf A1",
        DateTimeOffset? purchasedAt = null,
        InventoryFunctionalStatus functional = InventoryFunctionalStatus.FullyFunctional,
        InventoryOwnershipType ownership = InventoryOwnershipType.Owned,
        decimal? estimate = 30m,
        decimal? asking = null) =>
        new()
        {
            OrganizationId = org,
            Title = "Brass lamp",
            Status = status,
            PurchasePrice = cost,
            StorageLocation = location,
            PurchasedAt = purchasedAt ?? Now.AddDays(-5),
            FunctionalStatus = functional,
            OwnershipType = ownership,
            EstimatedSalePrice = estimate,
            SellerAskingPrice = asking,
        };

    private static ConsoleQueueDto Queue(OperatorConsoleDto console, string key) =>
        Assert.Single(console.Queues, queue => queue.Key == key);

    private static void SeedMixedInventory(HaulScoutDbContext db, Guid org)
    {
        db.InventoryItems.AddRange(
            Item(org),
            Item(org, InventoryStatus.NeedsRepair, functional: InventoryFunctionalStatus.NonFunctional),
            Item(org, InventoryStatus.NeedsPhotos, cost: null),
            Item(org, InventoryStatus.ReadyToList, location: null),
            Item(org, InventoryStatus.Listed, purchasedAt: Now.AddDays(-120)),
            Item(org, InventoryStatus.ReadyToList, purchasedAt: Now.AddDays(-90)),
            // Sold stock: must not surface in the stock-only hygiene queues.
            Item(org, InventoryStatus.Delivered, location: null, purchasedAt: Now.AddDays(-200)),
            Item(org, InventoryStatus.Sold, cost: null),
            Item(org, InventoryStatus.ReadyToList, ownership: InventoryOwnershipType.CustomerRepair));
        db.SaveChanges();
    }

    /// <summary>
    /// For each inventory queue, the filter the portal's link opens with. The portal maps keys to
    /// these same query strings; the admin test suite pins that half.
    /// </summary>
    public static TheoryData<string, InventoryFilter> InventoryQueues => new()
    {
        { "inventory.readyToList", new InventoryFilter(Status: InventoryStatus.ReadyToList) },
        { "inventory.benchWork", new InventoryFilter(NeedsBenchWork: true) },
        { "inventory.missingCost", new InventoryFilter(Attention: InventoryAttention.MissingCost) },
        { "inventory.missingPhotos", new InventoryFilter(Lifecycle: InventoryLifecycleGroup.Active, Attention: InventoryAttention.MissingPhotos) },
        { "inventory.missingLocation", new InventoryFilter(Lifecycle: InventoryLifecycleGroup.Active, Attention: InventoryAttention.MissingLocation) },
        { "inventory.aged", new InventoryFilter(Lifecycle: InventoryLifecycleGroup.Active, Attention: InventoryAttention.Aged) },
    };

    [Theory]
    [MemberData(nameof(InventoryQueues))]
    public async Task Each_inventory_queue_counts_exactly_what_its_list_shows(string key, InventoryFilter listFilter)
    {
        Harness harness = NewHarness(Owner);
        SeedMixedInventory(harness.Db, harness.Org);

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);
        InventoryListDto list = await harness.Inventory.BrowseAsync(
            listFilter.Status, listFilter.Lifecycle, listFilter.IncludeArchived, listFilter.Search, listFilter.Attention,
            sort: null, desc: true, page: 1, pageSize: 25, listFilter.Ownership, listFilter.NeedsBenchWork, listFilter.LotCandidates, CancellationToken.None);

        Assert.Equal(list.Total, Queue(console, key).Count);
    }

    [Fact]
    public async Task The_listings_queues_follow_the_permission_that_opens_the_pages_they_link_to()
    {
        // Both link to pages behind ManageListings. Showing them to someone holding only ManageInventory
        // offers work they cannot open; hiding them from someone holding only ManageListings buries work
        // that is theirs.
        Harness inventoryOnly = NewHarness(HaulScoutPermissions.ManageInventory);
        OperatorConsoleDto withoutListings = await inventoryOnly.Console.BuildAsync(CancellationToken.None);

        Assert.DoesNotContain(withoutListings.Queues, queue => queue.Key.StartsWith("listings.", StringComparison.Ordinal));
        Assert.Contains(withoutListings.Queues, queue => queue.Key == "inventory.readyToList");

        Harness listingsOnly = NewHarness(HaulScoutPermissions.ManageListings);
        OperatorConsoleDto withListings = await listingsOnly.Console.BuildAsync(CancellationToken.None);

        Assert.Equal(0, Queue(withListings, "listings.pendingDelist").Count);
        Assert.Equal(0, Queue(withListings, "listings.soldNotRecorded").Count);
        Assert.DoesNotContain(withListings.Queues, queue => queue.Key.StartsWith("inventory.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stock_hygiene_ignores_what_has_already_sold()
    {
        // A delivered item with no shelf location is not something anyone needs to go and fix.
        Harness harness = NewHarness(Owner);
        SeedMixedInventory(harness.Db, harness.Org);

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);

        Assert.Equal(1, Queue(console, "inventory.missingLocation").Count);

        // Aged is old stock that is not on sale: the 90-day ReadyToList item. The 120-day item is
        // listed — waiting on a buyer, not on the operator — and the 200-day one has been delivered.
        Assert.Equal(1, Queue(console, "inventory.aged").Count);
    }

    [Fact]
    public async Task Missing_cost_still_counts_sold_items()
    {
        // Unlike a missing photo, a missing cost on a sale makes that sale's profit wrong.
        Harness harness = NewHarness(Owner);
        SeedMixedInventory(harness.Db, harness.Org);

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);

        Assert.Equal(2, Queue(console, "inventory.missingCost").Count);
    }

    [Fact]
    public async Task Stock_figures_are_the_inventory_page_s_own()
    {
        Harness harness = NewHarness(Owner);
        SeedMixedInventory(harness.Db, harness.Org);

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);
        InventorySummaryDto active = await harness.Inventory.SummaryAsync(
            new InventoryFilter(Lifecycle: InventoryLifecycleGroup.Active), CancellationToken.None);

        Assert.NotNull(console.Inventory);
        Assert.Equal(active.Total, console.Inventory.ActiveItems);
        Assert.Equal(active.CapitalDeployed, console.Inventory.CapitalDeployed);
        Assert.Equal(active.EstimatedResaleValue, console.Inventory.EstimatedResaleValue);
    }

    [Fact]
    public async Task Order_queues_come_from_the_order_workflow()
    {
        Harness harness = NewHarness(Owner);
        harness.Db.Orders.AddRange(
            new Order { OrganizationId = harness.Org, OrderNumber = "SH-1", Status = OrderStatus.Paid, PaymentStatus = PaymentStatus.Paid },
            new Order { OrganizationId = harness.Org, OrderNumber = "SH-2", Status = OrderStatus.Paid, PaymentStatus = PaymentStatus.Paid },
            new Order { OrganizationId = harness.Org, OrderNumber = "SH-3", Status = OrderStatus.Pending, PaymentStatus = PaymentStatus.Pending });
        await harness.Db.SaveChangesAsync();

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);

        Assert.Equal(2, Queue(console, "orders.toPack").Count);
        Assert.Equal(1, Queue(console, "orders.awaitingPayment").Count);
        Assert.Equal(0, Queue(console, "orders.needsAttention").Count);
    }

    [Fact]
    public async Task New_offers_are_counted_across_every_offer()
    {
        Harness harness = NewHarness(Owner);
        for (int index = 0; index < 3; index++)
        {
            harness.Db.StorefrontOffers.Add(new StorefrontOffer
            {
                OrganizationId = harness.Org,
                StoreListingId = Guid.NewGuid(),
                InventoryItemId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                OfferAmount = 20m,
                Status = index == 0 ? StorefrontOfferStatus.Declined : StorefrontOfferStatus.New,
            });
        }
        await harness.Db.SaveChangesAsync();

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);

        Assert.Equal(2, Queue(console, "offers.new").Count);
    }

    [Fact]
    public async Task Unallocated_purchases_include_lots_never_itemized_and_ignore_rounding()
    {
        Harness harness = NewHarness(Owner);
        InventoryItem item = Item(harness.Org);
        harness.Db.InventoryItems.Add(item);

        // Recorded at the till, never itemized: the whole cost is still to allocate.
        harness.Db.AcquisitionLots.Add(new AcquisitionLot { OrganizationId = harness.Org, Name = "Estate sale", PurchaseSubtotal = 120m });

        // Allocated to within a fraction of a cent: not work anyone needs to do.
        AcquisitionLot rounded = new() { OrganizationId = harness.Org, Name = "Pallet", PurchaseSubtotal = 100m };
        rounded.Items.Add(new AcquisitionLotItem { OrganizationId = harness.Org, InventoryItemId = item.Id, AllocatedCost = 99.995m });
        harness.Db.AcquisitionLots.Add(rounded);
        await harness.Db.SaveChangesAsync();

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);

        ConsoleQueueDto queue = Queue(console, "purchases.unallocated");
        Assert.Equal(1, queue.Count);
        Assert.Equal(120m, queue.Amount);
    }

    [Fact]
    public async Task Money_is_the_reports_page_s_own_calculation()
    {
        Harness harness = NewHarness(Owner);
        InventoryItem item = Item(harness.Org, InventoryStatus.Delivered);
        harness.Db.InventoryItems.Add(item);
        harness.Db.Sales.Add(new Sale
        {
            OrganizationId = harness.Org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 80m,
            CostOfGoods = 10m,
            SoldAt = Now.AddDays(-3),
        });
        await harness.Db.SaveChangesAsync();

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);
        BusinessReportDto report = await new BusinessReportService(harness.Db, new StubCurrentUser(harness.Org))
            .BuildAsync(ReportRange.Trailing(DateOnly.FromDateTime(Now.UtcDateTime), OperatorConsoleService.MoneyWindowDays), CancellationToken.None);

        Assert.NotNull(console.Money);
        Assert.Equal(report.Kpis, console.Money.Current);
        Assert.Equal(report.Comparison, console.Money.Comparison);
        Assert.Equal(new DateOnly(2026, 8, 20), console.Money.From);
        Assert.Equal(new DateOnly(2026, 9, 18), console.Money.To);
    }

    [Fact]
    public async Task A_packer_sees_the_packing_queue_and_not_the_books()
    {
        // Each section shows only to someone who can open the page behind it — absent, not zero.
        Harness harness = NewHarness(HaulScoutPermissions.ManageOrders);
        SeedMixedInventory(harness.Db, harness.Org);

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);

        Assert.Contains(console.Queues, queue => queue.Key == "orders.toPack");
        Assert.DoesNotContain(console.Queues, queue => queue.Key.StartsWith("inventory.", StringComparison.Ordinal));
        Assert.DoesNotContain(console.Queues, queue => queue.Key.StartsWith("purchases.", StringComparison.Ordinal));
        Assert.Null(console.Inventory);
        Assert.Null(console.Money);
    }

    [Fact]
    public async Task A_bookkeeper_sees_the_money_and_not_the_warehouse()
    {
        Harness harness = NewHarness(HaulScoutPermissions.ViewReports, HaulScoutPermissions.ManageFinance);

        OperatorConsoleDto console = await harness.Console.BuildAsync(CancellationToken.None);

        Assert.NotNull(console.Money);
        Assert.Contains(console.Queues, queue => queue.Key == "purchases.unallocated");
        Assert.DoesNotContain(console.Queues, queue => queue.Key.StartsWith("orders.", StringComparison.Ordinal));
        Assert.Null(console.Inventory);
    }

    [Fact]
    public async Task Legacy_summary_counts_items_awaiting_repair_as_unlisted()
    {
        // The old home page's hardcoded list left NeedsRepair out, while the list its card opened
        // included it — so the card and the list disagreed by every item on the repair bench.
        Harness harness = NewHarness(Owner);
        SeedMixedInventory(harness.Db, harness.Org);

        DashboardSummaryDto summary = await harness.Console.LegacySummaryAsync(CancellationToken.None);
        InventoryListDto list = await harness.Inventory.BrowseAsync(
            null, null, false, null, InventoryAttention.Unlisted, null, true, 1, 25, null, false, false, CancellationToken.None);

        Assert.Equal(list.Total, summary.UnlistedInventoryCount);
        Assert.Contains(list.Items, item => item.Status == InventoryStatus.NeedsRepair);
    }

    [Fact]
    public async Task Legacy_summary_does_not_count_an_asking_price_as_money_spent()
    {
        Harness harness = NewHarness(Owner);
        harness.Db.InventoryItems.AddRange(
            Item(harness.Org, cost: 40m),
            // A consigned piece: the consignor's ask is not cash this business deployed.
            Item(harness.Org, cost: null, asking: 500m, ownership: InventoryOwnershipType.Consigned));
        await harness.Db.SaveChangesAsync();

        DashboardSummaryDto summary = await harness.Console.LegacySummaryAsync(CancellationToken.None);

        Assert.Equal(40m, summary.CashDeployed);
        Assert.Equal(40m, summary.TotalInventoryCost);
    }

    /// <summary>
    /// A context that behaves like the real one: nothing is known until <see cref="ResolveAsync"/>
    /// runs. The stubs above are always resolved, which is exactly why search shipped returning
    /// nothing — no test could see the difference.
    /// </summary>
    private sealed class LazyCurrentUser(Guid? membershipOrganization) : ICurrentUserContext
    {
        private bool resolved;

        public Guid OrganizationId => ActiveOrganizationId ?? throw new InvalidOperationException("No active organization.");
        public Guid? ActiveOrganizationId => resolved ? membershipOrganization : null;
        public Guid UserId => Guid.Empty;
        public Task ResolveAsync()
        {
            resolved = true;
            return Task.CompletedTask;
        }
    }

    private static AuthorizationFilterContext FilterContext(ICurrentUserContext currentUser)
    {
        ServiceCollection services = new();
        services.AddSingleton(currentUser);
        DefaultHttpContext http = new() { RequestServices = services.BuildServiceProvider() };
        ActionContext action = new(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(action, []);
    }

    [Fact]
    public async Task The_member_gate_resolves_the_caller_before_anything_reads_them()
    {
        LazyCurrentUser currentUser = new(Guid.NewGuid());
        AuthorizationFilterContext context = FilterContext(currentUser);

        await new RequireOrganizationMemberAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        Assert.NotNull(currentUser.ActiveOrganizationId);
    }

    [Fact]
    public async Task The_member_gate_refuses_someone_who_belongs_to_no_organization()
    {
        AuthorizationFilterContext context = FilterContext(new LazyCurrentUser(null));

        await new RequireOrganizationMemberAttribute().OnAuthorizationAsync(context);

        Assert.IsType<ForbidResult>(context.Result);
    }
}
