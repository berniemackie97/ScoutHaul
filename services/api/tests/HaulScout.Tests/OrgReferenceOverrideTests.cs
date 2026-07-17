using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class OrgReferenceOverrideTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, InventoryReferenceDataService Service, Guid Org) Setup([System.Runtime.CompilerServices.CallerMemberName] string store = "")
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, new InventoryReferenceDataService(db, new StubCurrentUser(org)), org);
    }

    [Fact]
    public async Task Org_can_add_a_custom_storage_location_and_hide_a_category()
    {
        (HaulScoutDbContext db, InventoryReferenceDataService service, Guid org) = Setup();
        db.OrgReferenceOverrides.Add(new OrgReferenceOverride { OrganizationId = org, Kind = "StorageLocation", Value = "Bin A3", IsCustom = true });
        db.OrgReferenceOverrides.Add(new OrgReferenceOverride { OrganizationId = org, Kind = "Category", Value = "Automotive", IsDisabled = true });
        await db.SaveChangesAsync();

        InventoryReferenceDataDto data = await service.GetAsync(default);

        Assert.Contains(data.StorageLocations, option => option.Value == "Bin A3");
        Assert.DoesNotContain(data.Categories, option => option.Value == "Automotive");
        // Untouched categories remain.
        Assert.Contains(data.Categories, option => option.Value == "Video Games");
    }

    [Fact]
    public async Task Overrides_are_scoped_to_the_organization()
    {
        (HaulScoutDbContext db, InventoryReferenceDataService service, _) = Setup();
        // An override for a different org must not leak.
        db.OrgReferenceOverrides.Add(new OrgReferenceOverride { OrganizationId = Guid.NewGuid(), Kind = "Category", Value = "Video Games", IsDisabled = true });
        await db.SaveChangesAsync();

        InventoryReferenceDataDto data = await service.GetAsync(default);
        Assert.Contains(data.Categories, option => option.Value == "Video Games");
    }
}
