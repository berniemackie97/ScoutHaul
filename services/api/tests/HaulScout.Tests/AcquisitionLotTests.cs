using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class AcquisitionLotTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, AcquisitionLotsController Controller, Guid Org) NewController([System.Runtime.CompilerServices.CallerMemberName] string store = "")
    {
        var org = Guid.NewGuid();
        var user = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store)
            // The in-memory provider has no real transactions; itemize uses one, so silence the warning under test.
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var tenant = new TenantContext();
        tenant.SetOrganization(org);
        var db = new HaulScoutDbContext(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        var controller = new AcquisitionLotsController(db, new StubCurrentUser(org, user));
        return (db, controller, org);
    }

    private static Guid SeedItem(HaulScoutDbContext db, Guid org, string title)
    {
        var item = new InventoryItem { OrganizationId = org, Title = title, Status = InventoryStatus.Draft };
        db.InventoryItems.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    private static UpsertAcquisitionLotRequest LotRequest(
        AcquisitionLotAllocationMethod method,
        decimal subtotal,
        params AcquisitionLotItemInputDto[] items) =>
        new("Test lot", DateTimeOffset.UtcNow.AddDays(-1), subtotal, 0m, 0m, 0m, 0m, method, null, null, null, null, null, null, items);

    private static AcquisitionLotDto Value(ActionResult<AcquisitionLotDto> result) =>
        result.Value ?? Assert.IsType<AcquisitionLotDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);

    [Fact]
    public async Task Create_allows_a_costs_only_lot_with_no_items()
    {
        var (db, controller, org) = NewController();

        var result = await controller.Create(LotRequest(AcquisitionLotAllocationMethod.Equal, 60m), default);
        var lot = Value(result);

        Assert.Empty(lot.Items);
        Assert.Equal(60m, lot.TotalLandedCost);
        Assert.Equal(0m, lot.TotalAllocatedCost);
        // A single purchase expense captures the full landed cost even before itemizing.
        Assert.Equal(60m, db.Expenses.Single(expense => expense.AcquisitionLotId == lot.Id).Amount);
    }

    [Fact]
    public async Task Equal_allocation_splits_landed_cost_evenly()
    {
        var (db, controller, org) = NewController();
        var a = SeedItem(db, org, "A");
        var b = SeedItem(db, org, "B");

        var result = await controller.Create(
            LotRequest(AcquisitionLotAllocationMethod.Equal, 100m, new AcquisitionLotItemInputDto(a), new AcquisitionLotItemInputDto(b)),
            default);
        var lot = Value(result);

        Assert.Equal(100m, lot.TotalAllocatedCost);
        Assert.Equal(50m, db.InventoryItems.Single(item => item.Id == a).PurchasePrice);
        Assert.Equal(50m, db.InventoryItems.Single(item => item.Id == b).PurchasePrice);
        // Items are promoted out of Draft once cost basis is assigned.
        Assert.Equal(InventoryStatus.Purchased, db.InventoryItems.Single(item => item.Id == a).Status);
    }

    [Fact]
    public async Task Itemize_creates_draft_items_and_reallocates_across_the_full_set()
    {
        var (db, controller, org) = NewController();
        var seeded = SeedItem(db, org, "Seed");
        var created = Value(await controller.Create(
            LotRequest(AcquisitionLotAllocationMethod.Equal, 120m, new AcquisitionLotItemInputDto(seeded)),
            default));
        Assert.Equal(120m, db.InventoryItems.Single(item => item.Id == seeded).PurchasePrice);

        var itemized = await controller.AddItems(
            created.Id,
            new AddLotItemsRequest([new NewLotItemDto("New A"), new NewLotItemDto("New B")]),
            default);
        var lot = itemized.Value!;

        // 3 items now share the $120 landed cost → $40 each.
        Assert.Equal(3, lot.Items.Count);
        Assert.All(db.InventoryItems.Where(item => db.AcquisitionLotItems.Any(a => a.InventoryItemId == item.Id)),
            item => Assert.Equal(40m, item.PurchasePrice));
        Assert.All(lot.Items, item => Assert.Equal(InventoryStatus.Purchased, item.InventoryItemStatus));
    }

    [Fact]
    public async Task Weighted_allocation_uses_expected_value()
    {
        var (db, controller, org) = NewController();
        var cheap = SeedItem(db, org, "Cheap");
        var pricey = SeedItem(db, org, "Pricey");

        var lot = Value(await controller.Create(
            LotRequest(
                AcquisitionLotAllocationMethod.WeightedByExpectedValue,
                100m,
                new AcquisitionLotItemInputDto(cheap, AllocationWeight: 25m),
                new AcquisitionLotItemInputDto(pricey, AllocationWeight: 75m)),
            default));

        Assert.Equal(25m, db.InventoryItems.Single(item => item.Id == cheap).PurchasePrice);
        Assert.Equal(75m, db.InventoryItems.Single(item => item.Id == pricey).PurchasePrice);
        Assert.Equal(100m, lot.TotalAllocatedCost);
    }
}
