using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// The same operations queries, against the engine that actually serves them.
///
/// The in-memory provider is LINQ-to-objects wearing a database costume: it happily runs a grouped
/// sum over a child collection that Npgsql cannot translate at all, so a query can pass every unit
/// test and then throw on the first real request. These rollups aggregate across navigations —
/// exactly the shape that does not always survive translation — so they are checked here too.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationsQueryTranslationTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Supplier_rollup_translates_to_sql()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        AcquisitionLot lot = new()
        {
            OrganizationId = org,
            Name = "Estate sale",
            SupplierName = "Maple St Estate",
            PurchaseSubtotal = 100m,
            ShippingCost = 20m,
            PurchasedAt = DateTimeOffset.UtcNow.AddDays(-3),
        };
        lot.Items.Add(new AcquisitionLotItem { OrganizationId = org, InventoryItemId = item.Id, AllocatedCost = 90m });
        db.AcquisitionLots.Add(lot);
        db.AcquisitionLots.Add(new AcquisitionLot { OrganizationId = org, Name = "Anonymous haul", PurchaseSubtotal = 30m });
        await db.SaveChangesAsync();

        AcquisitionLotsController controller = new(db, new StubCurrentUser(org, user));
        SupplierRollupListDto rollup = Value(await controller.Suppliers(CancellationToken.None));

        Assert.Equal(150m, rollup.TotalSpend);
        Assert.Equal(2, rollup.TotalLots);
        SupplierRollupDto maple = Assert.Single(rollup.Suppliers, supplier => supplier.Attributed);
        Assert.Equal(120m, maple.Spend);
        Assert.Equal(1, maple.Items);
        Assert.NotNull(maple.LastPurchase);
    }

    [Fact]
    public async Task Lot_browse_summary_translates_to_sql()
    {
        // The riskiest shape in the change: a sum over each lot's items inside a grouped aggregate.
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        AcquisitionLot lot = new() { OrganizationId = org, Name = "Estate sale", PurchaseSubtotal = 100m, ShippingCost = 20m };
        lot.Items.Add(new AcquisitionLotItem { OrganizationId = org, InventoryItemId = item.Id, AllocatedCost = 90m });
        db.AcquisitionLots.Add(lot);
        await db.SaveChangesAsync();

        AcquisitionLotsController controller = new(db, new StubCurrentUser(org, user));
        AcquisitionLotListDto result = Value(await controller.Browse(search: null, page: 1, pageSize: 25, CancellationToken.None));

        Assert.Equal(120m, result.Summary.TotalSpend);
        Assert.Equal(90m, result.Summary.TotalAllocated);
        Assert.Equal(30m, result.Summary.Unallocated);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task Lot_search_translates_to_sql_and_ignores_case()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        db.AcquisitionLots.AddRange(
            new AcquisitionLot { OrganizationId = org, Name = "Maple St estate", PurchaseSubtotal = 100m },
            new AcquisitionLot { OrganizationId = org, Name = "Auction pickup", SupplierName = "Northside", PurchaseSubtotal = 400m });
        await db.SaveChangesAsync();

        AcquisitionLotsController controller = new(db, new StubCurrentUser(org, user));

        Assert.Equal("Maple St estate", Single(await controller.Browse("MAPLE", 1, 25, CancellationToken.None)).Name);
        Assert.Equal("Auction pickup", Single(await controller.Browse("northside", 1, 25, CancellationToken.None)).Name);
    }

    [Fact]
    public async Task Bench_work_and_ownership_filters_translate_to_sql()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        db.InventoryItems.AddRange(
            new InventoryItem { OrganizationId = org, Title = "Untested amp", Status = InventoryStatus.ReadyToList, FunctionalStatus = InventoryFunctionalStatus.Untested },
            new InventoryItem { OrganizationId = org, Title = "Customer turntable", Status = InventoryStatus.ReadyToList, OwnershipType = InventoryOwnershipType.CustomerRepair },
            new InventoryItem { OrganizationId = org, Title = "Consigned chair", Status = InventoryStatus.ReadyToList, OwnershipType = InventoryOwnershipType.Consigned, SellerAskingPrice = 200m },
            new InventoryItem { OrganizationId = org, Title = "Working lamp", Status = InventoryStatus.ReadyToList, FunctionalStatus = InventoryFunctionalStatus.FullyFunctional });
        await db.SaveChangesAsync();

        InventoryQueryService queries = new(new EfInventoryStore(db), new StubCurrentUser(org, user));

        InventoryListDto bench = await queries.BrowseAsync(
            null, null, false, null, null, null, true, 1, 25, ownership: null, needsBenchWork: true, CancellationToken.None);
        Assert.Equal(2, bench.Total);
        Assert.Equal(1, bench.Summary.CustomerOwned);

        InventoryListDto consigned = await queries.BrowseAsync(
            null, null, false, null, null, null, true, 1, 25, InventoryOwnershipType.Consigned, false, CancellationToken.None);
        Assert.Equal(1, consigned.Total);
        Assert.Equal(200m, consigned.Summary.AskingValue);
    }

    [Fact]
    public async Task Order_browse_translates_to_sql()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        db.Orders.AddRange(
            new Order { OrganizationId = org, OrderNumber = "SH-0001", Status = OrderStatus.Paid, PaymentStatus = PaymentStatus.Paid, GrandTotal = 100m },
            new Order { OrganizationId = org, OrderNumber = "SH-0002", Status = OrderStatus.Pending, PaymentStatus = PaymentStatus.Pending, GrandTotal = 400m });
        await db.SaveChangesAsync();

        StubCurrentUser currentUser = new(org, user);
        OrdersWorkflowService orders = new(db, currentUser, new OrderAssembler(db, currentUser));

        OrderListDto all = await orders.BrowseAsync(null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(2, all.Total);
        Assert.Equal(100m, all.Summary.CapturedRevenue);

        OrderListDto searched = await orders.BrowseAsync(null, "SH-0002", null, 1, 25, CancellationToken.None);
        Assert.Equal("SH-0002", Assert.Single(searched.Items).OrderNumber);

        // Narrowing the view is a question about what to look at, not a claim that the rest of the
        // book stopped existing. A captured-revenue figure that falls as you type is worse than none,
        // and the first cut of this did exactly that.
        Assert.Equal(2, searched.Summary.TotalOrders);
        Assert.Equal(100m, searched.Summary.CapturedRevenue);
    }

    private static AcquisitionLotDto Single(ActionResult<AcquisitionLotListDto> result) =>
        Assert.Single(Value(result).Items);

    private static T Value<T>(ActionResult<T> result) => result.Value
        ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");
}
