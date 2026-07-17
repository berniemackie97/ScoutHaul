using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public class SalesChannelWorkflowServiceTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, SalesChannelWorkflowService Service, Guid Org) NewService(
        [System.Runtime.CompilerServices.CallerMemberName] string store = "")
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase($"{store}-{Guid.NewGuid()}")
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, new SalesChannelWorkflowService(db, new StubCurrentUser(org, user)), org);
    }

    [Fact]
    public async Task Reference_data_includes_legacy_channel_accounts()
    {
        (HaulScoutDbContext db, SalesChannelWorkflowService service, Guid org) = NewService();
        await using HaulScoutDbContext _ = db;
        db.ChannelAccounts.Add(new ChannelAccount
        {
            OrganizationId = org,
            Channel = "Grailed",
            DisplayName = "Grailed closet",
        });
        await db.SaveChangesAsync();

        SalesReferenceDataDto result = await service.ReferenceDataAsync(CancellationToken.None);

        SaleChannelOptionDto legacy = Assert.Single(result.Channels, channel => channel.Value == "Grailed");
        Assert.True(legacy.IsLegacy);
    }

    [Fact]
    public async Task Candidates_exclude_active_sales_but_include_released_sales()
    {
        (HaulScoutDbContext db, SalesChannelWorkflowService service, Guid org) = NewService();
        await using HaulScoutDbContext _ = db;
        InventoryItem clean = new() { OrganizationId = org, Title = "Ready item", Status = InventoryStatus.ReadyToList, PurchasePrice = 5m };
        InventoryItem held = new() { OrganizationId = org, Title = "Held item", Status = InventoryStatus.Sold, PurchasePrice = 10m };
        InventoryItem released = new() { OrganizationId = org, Title = "Returned item", Status = InventoryStatus.Returned, PurchasePrice = 12m };
        InventoryItem bundle = new() { OrganizationId = org, Title = "Bundle shell", Status = InventoryStatus.Bundled, PurchasePrice = 20m };
        db.InventoryItems.AddRange(clean, held, released, bundle);
        db.Sales.AddRange(
            new Sale { OrganizationId = org, InventoryItem = held, Platform = "eBay", SoldPrice = 25m, CostOfGoods = 10m, Status = SaleStatus.Delivered },
            new Sale { OrganizationId = org, InventoryItem = released, Platform = "eBay", SoldPrice = 30m, CostOfGoods = 12m, Status = SaleStatus.Returned });
        await db.SaveChangesAsync();

        IReadOnlyList<SaleCandidateDto> result = await service.CandidatesAsync(CancellationToken.None);

        Assert.Contains(result, item => item.InventoryItemId == clean.Id);
        Assert.Contains(result, item => item.InventoryItemId == released.Id);
        Assert.DoesNotContain(result, item => item.InventoryItemId == held.Id);
        Assert.DoesNotContain(result, item => item.InventoryItemId == bundle.Id);
    }

    [Fact]
    public async Task Fee_estimate_validates_channel_and_profile_in_one_place()
    {
        (_, SalesChannelWorkflowService service, _) = NewService();

        AppResult<ChannelFeeEstimateDto> invalidChannel =
            await service.EstimateFeesAsync(new EstimateChannelFeesRequest("not-a-channel", "standard-marketplace", 10m), CancellationToken.None);
        AppResult<ChannelFeeEstimateDto> invalidProfile =
            await service.EstimateFeesAsync(new EstimateChannelFeesRequest("eBay", "not-a-profile", 10m), CancellationToken.None);
        AppResult<ChannelFeeEstimateDto> valid =
            await service.EstimateFeesAsync(new EstimateChannelFeesRequest("eBay", "standard-marketplace", 10m), CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, invalidChannel.Status);
        Assert.Equal(AppResultStatus.Validation, invalidProfile.Status);
        Assert.Equal(AppResultStatus.Ok, valid.Status);
        Assert.NotNull(valid.Value);
    }
}
