using Microsoft.EntityFrameworkCore;
using ScoutHaul.Application.Common;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Tests;

/// <summary>
/// Proves the data layer enforces organization (tenant) isolation via EF Core global query
/// filters, so business data from one organization can never be read under another's scope.
/// </summary>
public class OrganizationScopingTests
{
    private static DbContextOptions<ScoutHaulDbContext> SharedStore(string name) =>
        new DbContextOptionsBuilder<ScoutHaulDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

    private static ITenantContext Tenant(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return tenant;
    }

    [Fact]
    public async Task Queries_only_return_the_active_organizations_rows()
    {
        Guid orgA = Guid.NewGuid();
        Guid orgB = Guid.NewGuid();
        DbContextOptions<ScoutHaulDbContext> store = SharedStore(nameof(Queries_only_return_the_active_organizations_rows));

        await using (var db = new ScoutHaulDbContext(store, Tenant(orgA)))
        {
            db.InventoryItems.Add(new InventoryItem { Title = "Org A widget" });
            await db.SaveChangesAsync();
        }

        await using (var db = new ScoutHaulDbContext(store, Tenant(orgB)))
        {
            db.InventoryItems.Add(new InventoryItem { Title = "Org B widget" });
            await db.SaveChangesAsync();
        }

        await using (var db = new ScoutHaulDbContext(store, Tenant(orgA)))
        {
            List<InventoryItem> items = await db.InventoryItems.ToListAsync();
            InventoryItem only = Assert.Single(items);
            Assert.Equal("Org A widget", only.Title);
            Assert.Equal(orgA, only.OrganizationId);
        }

        await using (var db = new ScoutHaulDbContext(store, Tenant(orgB)))
        {
            List<InventoryItem> items = await db.InventoryItems.ToListAsync();
            InventoryItem only = Assert.Single(items);
            Assert.Equal("Org B widget", only.Title);
        }
    }

    [Fact]
    public async Task Tenant_A_cannot_read_tenant_B_row_by_id()
    {
        Guid orgA = Guid.NewGuid();
        Guid orgB = Guid.NewGuid();
        DbContextOptions<ScoutHaulDbContext> store = SharedStore(nameof(Tenant_A_cannot_read_tenant_B_row_by_id));

        Guid bItemId;
        await using (var db = new ScoutHaulDbContext(store, Tenant(orgB)))
        {
            var item = new InventoryItem { Title = "Org B secret" };
            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();
            bItemId = item.Id;
        }

        // Tenant A explicitly fetches B's row by primary key — the filter must still hide it.
        await using (var db = new ScoutHaulDbContext(store, Tenant(orgA)))
        {
            InventoryItem? leaked = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == bItemId);
            Assert.Null(leaked);
        }
    }

    [Fact]
    public async Task New_entities_are_auto_assigned_the_active_organization()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<ScoutHaulDbContext> store = SharedStore(nameof(New_entities_are_auto_assigned_the_active_organization));

        await using var db = new ScoutHaulDbContext(store, Tenant(org));
        var item = new InventoryItem { Title = "no org set" }; // OrganizationId left default (Guid.Empty)
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        Assert.Equal(org, item.OrganizationId);
    }

    [Fact]
    public async Task Context_without_a_tenant_is_unfiltered()
    {
        Guid orgA = Guid.NewGuid();
        Guid orgB = Guid.NewGuid();
        DbContextOptions<ScoutHaulDbContext> store = SharedStore(nameof(Context_without_a_tenant_is_unfiltered));

        await using (var db = new ScoutHaulDbContext(store, Tenant(orgA)))
        {
            db.InventoryItems.Add(new InventoryItem { Title = "A" });
            await db.SaveChangesAsync();
        }

        await using (var db = new ScoutHaulDbContext(store, Tenant(orgB)))
        {
            db.InventoryItems.Add(new InventoryItem { Title = "B" });
            await db.SaveChangesAsync();
        }

        // No tenant service (design-time / unit tests): filters are off so existing
        // tooling and tests keep seeing all rows.
        await using (var db = new ScoutHaulDbContext(store))
        {
            Assert.Equal(2, await db.InventoryItems.CountAsync());
        }
    }
}
