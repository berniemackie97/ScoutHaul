using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class SaleGroupingTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org, Guid User) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static Sale SeedSale(HaulScoutDbContext db, Guid org, string title)
    {
        InventoryItem item = new() { OrganizationId = org, Title = title, PurchasePrice = 10m, Status = InventoryStatus.Sold };
        db.InventoryItems.Add(item);
        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Platform = "eBay",
            SoldPrice = 50m,
            CostOfGoods = 10m,
            NetProfit = 40m,
            Status = SaleStatus.Delivered,
            InventoryItem = item,
        };
        db.Sales.Add(sale);
        db.SaveChanges();
        return sale;
    }

    [Fact]
    public async Task Combining_sales_assigns_a_shared_group_and_reference()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Combining_sales_assigns_a_shared_group_and_reference));
        await using HaulScoutDbContext _ = db;
        Sale a = SeedSale(db, org, "Item A");
        Sale b = SeedSale(db, org, "Item B");
        SalesController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<SaleGroupResultDto> result = await controller.GroupIntoOrder(new GroupSalesRequest([a.Id, b.Id], "12-34567-89012"), CancellationToken.None);

        SaleGroupResultDto group = result.Value!;
        Assert.NotEqual(Guid.Empty, group.SaleGroupId);
        Assert.Equal("12-34567-89012", group.OrderReference);
        Assert.All(group.Sales, sale => Assert.Equal(group.SaleGroupId, sale.SaleGroupId));
        List<Sale> stored = await db.Sales.ToListAsync();
        Assert.All(stored, sale => Assert.Equal(group.SaleGroupId, sale.SaleGroupId));
    }

    [Fact]
    public async Task Grouping_a_sale_into_an_existing_order_merges_rather_than_creating_a_new_group()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Grouping_a_sale_into_an_existing_order_merges_rather_than_creating_a_new_group));
        await using HaulScoutDbContext _ = db;
        Sale a = SeedSale(db, org, "Item A");
        Sale b = SeedSale(db, org, "Item B");
        Sale c = SeedSale(db, org, "Item C");
        SalesController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<SaleGroupResultDto> first = await controller.GroupIntoOrder(new GroupSalesRequest([a.Id, b.Id], "ORDER-1"), CancellationToken.None);
        Guid groupId = first.Value!.SaleGroupId;

        // Now add C to the same order by grouping it with an already-grouped sale.
        ActionResult<SaleGroupResultDto> merged = await controller.GroupIntoOrder(new GroupSalesRequest([b.Id, c.Id], null), CancellationToken.None);

        Assert.Equal(groupId, merged.Value!.SaleGroupId);            // reused, not a new group
        Assert.Equal("ORDER-1", merged.Value.OrderReference);         // inherited existing reference
        Assert.Equal(3, await db.Sales.CountAsync(sale => sale.SaleGroupId == groupId));
    }

    [Fact]
    public async Task Removing_a_sale_dissolves_a_group_left_with_one_member()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Removing_a_sale_dissolves_a_group_left_with_one_member));
        await using HaulScoutDbContext _ = db;
        Sale a = SeedSale(db, org, "Item A");
        Sale b = SeedSale(db, org, "Item B");
        SalesController controller = new(db, new StubCurrentUser(org, user));
        await controller.GroupIntoOrder(new GroupSalesRequest([a.Id, b.Id], "ORDER-1"), CancellationToken.None);

        await controller.RemoveFromOrder(new UngroupSalesRequest([a.Id]), CancellationToken.None);

        // A left ungrouped, and B — now alone — is dissolved too (a one-item group is not an order).
        Assert.Null((await db.Sales.SingleAsync(sale => sale.Id == a.Id)).SaleGroupId);
        Assert.Null((await db.Sales.SingleAsync(sale => sale.Id == b.Id)).SaleGroupId);
    }

    [Fact]
    public async Task Removing_one_of_three_keeps_the_remaining_two_grouped()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Removing_one_of_three_keeps_the_remaining_two_grouped));
        await using HaulScoutDbContext _ = db;
        Sale a = SeedSale(db, org, "Item A");
        Sale b = SeedSale(db, org, "Item B");
        Sale c = SeedSale(db, org, "Item C");
        SalesController controller = new(db, new StubCurrentUser(org, user));
        Guid group = (await controller.GroupIntoOrder(new GroupSalesRequest([a.Id, b.Id, c.Id], "ORDER-1"), CancellationToken.None)).Value!.SaleGroupId;

        await controller.RemoveFromOrder(new UngroupSalesRequest([a.Id]), CancellationToken.None);

        Assert.Null((await db.Sales.SingleAsync(sale => sale.Id == a.Id)).SaleGroupId);
        Assert.Equal(group, (await db.Sales.SingleAsync(sale => sale.Id == b.Id)).SaleGroupId);
        Assert.Equal(group, (await db.Sales.SingleAsync(sale => sale.Id == c.Id)).SaleGroupId);
    }

    [Fact]
    public async Task Combining_fewer_than_two_sales_is_rejected()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Combining_fewer_than_two_sales_is_rejected));
        await using HaulScoutDbContext _ = db;
        Sale a = SeedSale(db, org, "Item A");
        SalesController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<SaleGroupResultDto> result = await controller.GroupIntoOrder(new GroupSalesRequest([a.Id], null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
