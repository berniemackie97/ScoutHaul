using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Storefront;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Storefront;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class StoreSettingsTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store)
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, org);
    }

    [Fact]
    public async Task Get_returns_the_current_org_store_identity()
    {
        (HaulScoutDbContext db, Guid org) = NewContext(nameof(Get_returns_the_current_org_store_identity));
        await using HaulScoutDbContext _ = db;
        Organization existing = await db.Organizations.FirstAsync(o => o.Id == org);
        existing.StoreDomain = "overhaultrading.com";
        await db.SaveChangesAsync();

        StoreSettingsController controller = new(new StoreSettingsWorkflowService(new EfStoreSettingsStore(db), new StubCurrentUser(org)));
        ActionResult<StoreSettingsDto> result = await controller.Get(CancellationToken.None);

        Assert.Null(result.Result);
        StoreSettingsDto dto = result.Value!;
        Assert.Equal("Overhaul Trading Company", dto.Name);
        Assert.Equal("overhaultrading.com", dto.StoreDomain);
        Assert.True(dto.IsLive);
    }

    [Theory]
    [InlineData("https://www.OverhaulTrading.com/shop", "overhaultrading.com")]
    [InlineData("  Shop.OverhaulTrading.com  ", "shop.overhaultrading.com")]
    [InlineData("overhaultrading.com:443", "overhaultrading.com")]
    [InlineData("overhaultrading.com.", "overhaultrading.com")]
    public async Task Update_normalizes_and_saves_a_valid_domain(string input, string expected)
    {
        (HaulScoutDbContext db, Guid org) = NewContext($"normalize-{expected}-{input.GetHashCode()}");
        await using HaulScoutDbContext _ = db;

        StoreSettingsController controller = new(new StoreSettingsWorkflowService(new EfStoreSettingsStore(db), new StubCurrentUser(org)));
        ActionResult<StoreSettingsDto> result = await controller.Update(
            new UpdateStoreSettingsRequest("Overhaul Trading Company", input), CancellationToken.None);

        Assert.Null(result.Result);
        Assert.Equal(expected, result.Value!.StoreDomain);
        Assert.True(result.Value!.IsLive);

        Organization reloaded = await db.Organizations.FirstAsync(o => o.Id == org);
        Assert.Equal(expected, reloaded.StoreDomain);
    }

    [Theory]
    [InlineData("not a domain")]
    [InlineData("localhost")]
    [InlineData("http://")]
    [InlineData("overhaultrading")]
    public async Task Update_rejects_an_invalid_domain(string input)
    {
        (HaulScoutDbContext db, Guid org) = NewContext($"invalid-{input.GetHashCode()}");
        await using HaulScoutDbContext _ = db;

        StoreSettingsController controller = new(new StoreSettingsWorkflowService(new EfStoreSettingsStore(db), new StubCurrentUser(org)));
        ActionResult<StoreSettingsDto> result = await controller.Update(
            new UpdateStoreSettingsRequest("Overhaul Trading Company", input), CancellationToken.None);

        ProblemAssert.Invalid(result.Result);
    }

    [Fact]
    public async Task Update_rejects_a_domain_already_connected_to_another_org()
    {
        (HaulScoutDbContext db, Guid org) = NewContext(nameof(Update_rejects_a_domain_already_connected_to_another_org));
        await using HaulScoutDbContext _ = db;
        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), Name = "Another Co", StoreDomain = "overhaultrading.com" });
        await db.SaveChangesAsync();

        StoreSettingsController controller = new(new StoreSettingsWorkflowService(new EfStoreSettingsStore(db), new StubCurrentUser(org)));
        ActionResult<StoreSettingsDto> result = await controller.Update(
            new UpdateStoreSettingsRequest("Overhaul Trading Company", "overhaultrading.com"), CancellationToken.None);

        ProblemAssert.Blocked(result.Result);
    }

    [Fact]
    public async Task Update_with_blank_domain_disconnects_the_storefront()
    {
        (HaulScoutDbContext db, Guid org) = NewContext(nameof(Update_with_blank_domain_disconnects_the_storefront));
        await using HaulScoutDbContext _ = db;
        Organization existing = await db.Organizations.FirstAsync(o => o.Id == org);
        existing.StoreDomain = "overhaultrading.com";
        await db.SaveChangesAsync();

        StoreSettingsController controller = new(new StoreSettingsWorkflowService(new EfStoreSettingsStore(db), new StubCurrentUser(org)));
        ActionResult<StoreSettingsDto> result = await controller.Update(
            new UpdateStoreSettingsRequest("Overhaul Trading Company", "   "), CancellationToken.None);

        Assert.Null(result.Result);
        Assert.Null(result.Value!.StoreDomain);
        Assert.False(result.Value!.IsLive);
    }

    [Fact]
    public async Task Update_requires_a_store_name()
    {
        (HaulScoutDbContext db, Guid org) = NewContext(nameof(Update_requires_a_store_name));
        await using HaulScoutDbContext _ = db;

        StoreSettingsController controller = new(new StoreSettingsWorkflowService(new EfStoreSettingsStore(db), new StubCurrentUser(org)));
        ActionResult<StoreSettingsDto> result = await controller.Update(
            new UpdateStoreSettingsRequest("  ", "overhaultrading.com"), CancellationToken.None);

        ProblemAssert.Invalid(result.Result);
    }
}
