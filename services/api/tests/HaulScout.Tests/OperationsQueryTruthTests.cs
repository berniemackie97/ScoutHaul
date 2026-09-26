using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// The operations pages reported a window and called it the business.
///
/// Repairs, Consignments and Suppliers each fetched a capped list — 200 rows — and then filtered and
/// summed it in the browser. Past that many items the queue silently dropped rows and the money
/// figures beside it were simply wrong, with nothing on screen saying so. These are the server-side
/// answers those pages need: filtered by the thing the page is about, counted over everything.
/// </summary>
public sealed class OperationsQueryTruthTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public required HaulScoutDbContext Db { get; init; }
        public required Guid Org { get; init; }
        public required InventoryQueryService Inventory { get; init; }
        public required AcquisitionLotsController Lots { get; init; }
    }

    private static Harness NewHarness()
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
        return new Harness
        {
            Db = db,
            Org = org,
            Inventory = new InventoryQueryService(new EfInventoryStore(db), currentUser),
            Lots = new AcquisitionLotsController(db, currentUser),
        };
    }

    private static InventoryItem Item(
        Guid org,
        string title,
        InventoryFunctionalStatus functional = InventoryFunctionalStatus.FullyFunctional,
        InventoryOwnershipType ownership = InventoryOwnershipType.Owned,
        decimal? asking = null,
        decimal? estimated = null) =>
        new()
        {
            OrganizationId = org,
            Title = title,
            Status = InventoryStatus.ReadyToList,
            FunctionalStatus = functional,
            OwnershipType = ownership,
            SellerAskingPrice = asking,
            EstimatedSalePrice = estimated,
        };

    private static Task<InventoryListDto> Browse(
        Harness harness,
        InventoryOwnershipType? ownership = null,
        bool needsBenchWork = false,
        int pageSize = 25) =>
        harness.Inventory.BrowseAsync(
            status: null,
            lifecycle: null,
            includeArchived: false,
            search: null,
            attention: null,
            sort: null,
            desc: true,
            page: 1,
            pageSize: pageSize,
            ownership: ownership,
            needsBenchWork: needsBenchWork,
            lotCandidates: false,
            CancellationToken.None);

    [Fact]
    public async Task Bench_queue_is_the_condition_or_whose_item_it_is()
    {
        Harness harness = NewHarness();
        harness.Db.InventoryItems.AddRange(
            Item(harness.Org, "Untested amp", InventoryFunctionalStatus.Untested),
            Item(harness.Org, "Dead radio", InventoryFunctionalStatus.NonFunctional),
            Item(harness.Org, "Intermittent drill", InventoryFunctionalStatus.Intermittent),
            Item(harness.Org, "Scratchy mixer", InventoryFunctionalStatus.FunctionalWithIssues),
            Item(harness.Org, "Customer's turntable", ownership: InventoryOwnershipType.CustomerRepair),
            Item(harness.Org, "Working lamp"),
            Item(harness.Org, "Parts donor", InventoryFunctionalStatus.PartsOnly));
        await harness.Db.SaveChangesAsync();

        InventoryListDto result = await Browse(harness, needsBenchWork: true);

        Assert.Equal(5, result.Total);
        Assert.DoesNotContain(result.Items, item => item.Title == "Working lamp");
        // Kept for parts is not waiting on a repair — it is already what it is going to be.
        Assert.DoesNotContain(result.Items, item => item.Title == "Parts donor");
    }

    [Fact]
    public async Task Bench_queue_counts_every_match_not_the_page_shown()
    {
        // The whole point. A one-row page must still report the size of the queue behind it.
        Harness harness = NewHarness();
        for (int index = 0; index < 40; index++)
        {
            harness.Db.InventoryItems.Add(Item(harness.Org, $"Untested {index}", InventoryFunctionalStatus.Untested));
        }
        await harness.Db.SaveChangesAsync();

        InventoryListDto result = await Browse(harness, needsBenchWork: true, pageSize: 1);

        Assert.Single(result.Items);
        Assert.Equal(40, result.Total);
        Assert.Equal(40, result.Summary.NeedsBenchWork);
    }

    [Fact]
    public async Task Consigned_money_is_reported_two_ways_without_one_standing_in_for_the_other()
    {
        Harness harness = NewHarness();
        harness.Db.InventoryItems.AddRange(
            Item(harness.Org, "Consigned chair", ownership: InventoryOwnershipType.Consigned, asking: 200m, estimated: 260m),
            // Asked for, not yet valued: it counts toward the asks and not toward the estimate.
            Item(harness.Org, "Consigned rug", ownership: InventoryOwnershipType.Consigned, asking: 100m),
            Item(harness.Org, "Our own lamp", asking: 999m, estimated: 999m));
        await harness.Db.SaveChangesAsync();

        InventoryListDto result = await Browse(harness, ownership: InventoryOwnershipType.Consigned);

        Assert.Equal(2, result.Total);
        Assert.Equal(300m, result.Summary.AskingValue);
        // The estimate falls back to the ask when there is no estimate; the asks never fall back.
        Assert.Equal(360m, result.Summary.EstimatedResaleValue);
    }

    [Fact]
    public async Task Ownership_filter_leaves_other_people_s_items_out()
    {
        Harness harness = NewHarness();
        harness.Db.InventoryItems.AddRange(
            Item(harness.Org, "Customer's turntable", ownership: InventoryOwnershipType.CustomerRepair),
            Item(harness.Org, "Consigned chair", ownership: InventoryOwnershipType.Consigned),
            Item(harness.Org, "Our own lamp"));
        await harness.Db.SaveChangesAsync();

        InventoryListDto result = await Browse(harness, ownership: InventoryOwnershipType.CustomerRepair);

        Assert.Equal("Customer's turntable", Assert.Single(result.Items).Title);
        Assert.Equal(1, result.Summary.CustomerOwned);
    }

    [Fact]
    public async Task Supplier_spend_covers_every_lot_ever_recorded()
    {
        Harness harness = NewHarness();
        // More lots than the list endpoint would ever return, which is exactly the case the browser
        // rollup got wrong while presenting its answer as total spend.
        for (int index = 0; index < 250; index++)
        {
            harness.Db.AcquisitionLots.Add(new AcquisitionLot
            {
                OrganizationId = harness.Org,
                Name = $"Lot {index}",
                SupplierName = "Maple St Estate",
                PurchaseSubtotal = 10m,
                ShippingCost = 1m,
            });
        }
        await harness.Db.SaveChangesAsync();

        SupplierRollupListDto rollup = Value(await harness.Lots.Suppliers(CancellationToken.None));

        Assert.Equal(250, rollup.TotalLots);
        Assert.Equal(2750m, rollup.TotalSpend);
        Assert.Equal(250, Assert.Single(rollup.Suppliers).Lots);
    }

    [Fact]
    public async Task Unnamed_and_blank_suppliers_land_in_one_bucket()
    {
        // Grouped in SQL, null and "" are different keys, so the unattributed spend arrived split in
        // two and one half was labelled with an empty name.
        Harness harness = NewHarness();
        harness.Db.AcquisitionLots.AddRange(
            new AcquisitionLot { OrganizationId = harness.Org, Name = "A", SupplierName = null, PurchaseSubtotal = 10m },
            new AcquisitionLot { OrganizationId = harness.Org, Name = "B", SupplierName = "", PurchaseSubtotal = 20m },
            new AcquisitionLot { OrganizationId = harness.Org, Name = "C", SupplierName = "   ", PurchaseSubtotal = 30m },
            new AcquisitionLot { OrganizationId = harness.Org, Name = "D", SupplierName = "Maple St", PurchaseSubtotal = 40m });
        await harness.Db.SaveChangesAsync();

        SupplierRollupListDto rollup = Value(await harness.Lots.Suppliers(CancellationToken.None));

        SupplierRollupDto unattributed = Assert.Single(rollup.Suppliers, supplier => !supplier.Attributed);
        Assert.Equal(60m, unattributed.Spend);
        Assert.Equal(3, unattributed.Lots);
        Assert.Equal(1, rollup.AttributedSuppliers);
    }

    [Fact]
    public async Task Suppliers_are_ranked_by_what_was_spent_with_them()
    {
        Harness harness = NewHarness();
        harness.Db.AcquisitionLots.AddRange(
            new AcquisitionLot { OrganizationId = harness.Org, Name = "A", SupplierName = "Small", PurchaseSubtotal = 10m },
            new AcquisitionLot { OrganizationId = harness.Org, Name = "B", SupplierName = "Big", PurchaseSubtotal = 500m });
        await harness.Db.SaveChangesAsync();

        SupplierRollupListDto rollup = Value(await harness.Lots.Suppliers(CancellationToken.None));

        Assert.Equal(["Big", "Small"], rollup.Suppliers.Select(supplier => supplier.Supplier));
    }

    [Fact]
    public async Task Purchase_spend_covers_every_lot_not_the_page_shown()
    {
        Harness harness = NewHarness();
        for (int index = 0; index < 250; index++)
        {
            harness.Db.AcquisitionLots.Add(new AcquisitionLot
            {
                OrganizationId = harness.Org,
                Name = $"Lot {index}",
                PurchaseSubtotal = 20m,
            });
        }
        await harness.Db.SaveChangesAsync();

        AcquisitionLotListDto result = Value(
            await harness.Lots.Browse(search: null, page: 1, pageSize: 25, CancellationToken.None));

        Assert.Equal(25, result.Items.Count);
        Assert.Equal(250, result.Total);
        Assert.Equal(250, result.Summary.TotalLots);
        Assert.Equal(5000m, result.Summary.TotalSpend);
    }

    [Fact]
    public async Task Unallocated_cost_is_the_work_still_to_do()
    {
        Harness harness = NewHarness();
        InventoryItem item = new() { OrganizationId = harness.Org, Title = "Brass Lamp", Status = InventoryStatus.ReadyToList };
        harness.Db.InventoryItems.Add(item);
        AcquisitionLot lot = new() { OrganizationId = harness.Org, Name = "Estate sale", PurchaseSubtotal = 100m, ShippingCost = 20m };
        lot.Items.Add(new AcquisitionLotItem
        {
            OrganizationId = harness.Org,
            InventoryItemId = item.Id,
            AllocatedCost = 80m,
        });
        harness.Db.AcquisitionLots.Add(lot);
        await harness.Db.SaveChangesAsync();

        AcquisitionLotListDto result = Value(
            await harness.Lots.Browse(search: null, page: 1, pageSize: 25, CancellationToken.None));

        Assert.Equal(120m, result.Summary.TotalSpend);
        Assert.Equal(80m, result.Summary.TotalAllocated);
        Assert.Equal(40m, result.Summary.Unallocated);
    }

    [Fact]
    public async Task Narrowing_the_lot_list_does_not_narrow_the_money()
    {
        // The search filters what is listed. It must not quietly redefine "total purchase spend".
        Harness harness = NewHarness();
        harness.Db.AcquisitionLots.AddRange(
            new AcquisitionLot { OrganizationId = harness.Org, Name = "Maple St estate", PurchaseSubtotal = 100m },
            new AcquisitionLot { OrganizationId = harness.Org, Name = "Auction pickup", PurchaseSubtotal = 400m });
        await harness.Db.SaveChangesAsync();

        AcquisitionLotListDto result = Value(
            await harness.Lots.Browse("Maple", page: 1, pageSize: 25, CancellationToken.None));

        Assert.Equal(1, result.Total);
        Assert.Equal(500m, result.Summary.TotalSpend);
    }

    private static T Value<T>(ActionResult<T> result) => result.Value
        ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name}");
}
