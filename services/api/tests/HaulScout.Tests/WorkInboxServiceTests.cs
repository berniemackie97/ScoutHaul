using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Api.Services;
using HaulScout.Application.Channels;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Application.Sync;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Channels;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Sync;
using HaulScout.Infrastructure.Work;

namespace HaulScout.Tests;

public sealed class WorkInboxServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);

    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class AllowAll : IOrganizationAuthorizer
    {
        public Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListPermissions(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task RequireOwner(Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static (WorkInboxService Work, HaulScoutDbContext Db, Guid Org) Harness()
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Org" });
        db.SaveChanges();
        StubCurrentUser user = new(org);
        OrderAssembler assembler = new(db, user);
        FakeTimeProvider clock = new(Now);
        WorkInboxService work = new(
            user,
            new AllowAll(),
            OrdersServiceFactory.Create(db, user, assembler),
            new InventoryQueryService(new EfInventoryStore(db, clock), user),
            new EfPlatformOrderStore(db, assembler),
            new ChannelDelistService(new EfChannelDelistStore(db), user, clock),
            new SoldListingService(new EfSoldListingStore(db), user, clock),
            new SyncIssueService(new EfSyncIssueStore(db), null!, user, clock),
            new EfWorkSnoozeStore(db),
            new EfListingMatchStore(db),
            clock);
        return (work, db, org);
    }

    private static Order Paid(Guid org, string number, DateTimeOffset paidAt) => new()
    {
        OrganizationId = org,
        OrderNumber = number,
        Status = OrderStatus.Paid,
        PaymentStatus = PaymentStatus.Paid,
        PaidAt = paidAt,
    };

    [Fact]
    public async Task Paid_orders_land_in_pack_with_their_thread_and_late_ones_first()
    {
        (WorkInboxService work, HaulScoutDbContext db, Guid org) = Harness();
        db.Orders.AddRange(Paid(org, "SH-1", Now.AddHours(-3)), Paid(org, "SH-2", Now.AddDays(-4)));
        await db.SaveChangesAsync();

        WorkInboxDto inbox = await work.BuildAsync(CancellationToken.None);

        WorkLaneDto pack = inbox.Lanes.Single(lane => lane.Key == WorkInboxService.Pack);
        Assert.Equal(2, pack.Tasks.Count);
        Assert.True(pack.Tasks[0].IsLate);
        Assert.Contains("SH-2", pack.Tasks[0].Detail);
        Assert.Equal(1, inbox.LateCount);
        WorkStepDto now = Assert.Single(pack.Tasks[0].Steps, step => step.State == WorkStepState.Now);
        Assert.StartsWith("Packed", now.Label);
        Assert.StartsWith("/work/pack?order=", pack.Tasks[0].ActionHref);
    }

    [Fact]
    public async Task A_snoozed_task_leaves_its_lane_and_shows_as_waiting_until_its_date()
    {
        (WorkInboxService work, HaulScoutDbContext db, Guid org) = Harness();
        Order order = Paid(org, "SH-9", Now.AddHours(-1));
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        string key = $"order:{order.Id}:pack";

        AppResult refused = await work.SetAsideAsync(new SetAsideWorkTaskRequest(key, WorkWait.Snoozed), CancellationToken.None);
        Assert.Equal(AppResultStatus.Validation, refused.Status);

        await work.SetAsideAsync(new SetAsideWorkTaskRequest(key, WorkWait.Snoozed, Now.AddDays(1), "Waiting on a box", "Pack SH-9"), CancellationToken.None);
        WorkInboxDto inbox = await work.BuildAsync(CancellationToken.None);

        Assert.Empty(inbox.Lanes.Single(lane => lane.Key == WorkInboxService.Pack).Tasks);
        WorkWaitDto waiting = Assert.Single(inbox.Waiting);
        Assert.Equal("Waiting on a box", waiting.Note);

        await work.BringBackAsync(key, CancellationToken.None);
        Assert.Single((await work.BuildAsync(CancellationToken.None)).Lanes.Single(lane => lane.Key == WorkInboxService.Pack).Tasks);
    }

    [Fact]
    public async Task Ready_items_and_open_sync_issues_each_land_in_their_lane()
    {
        (WorkInboxService work, HaulScoutDbContext db, Guid org) = Harness();
        db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "Carhartt jacket", Status = InventoryStatus.ReadyToList, StorageLocation = "C-08" });
        db.SyncRunIssues.Add(new SyncRunIssue
        {
            OrganizationId = org,
            SyncRun = new SyncRun { OrganizationId = org, FormatKey = "agent", Platform = "Vinted", StartedAt = Now },
            Code = SyncIssueCodes.SiteChanged,
            Severity = SyncIssueSeverity.Error,
            Message = "unreadable",
        });
        await db.SaveChangesAsync();

        WorkInboxDto inbox = await work.BuildAsync(CancellationToken.None);

        Assert.Contains(inbox.Lanes.Single(lane => lane.Key == WorkInboxService.List).Tasks, task => task.Title.Contains("Carhartt"));
        Assert.Contains(inbox.Lanes.Single(lane => lane.Key == WorkInboxService.FollowUp).Tasks, task => task.Kind == "Sync issue" && task.Title.Contains("Vinted"));
    }
}
