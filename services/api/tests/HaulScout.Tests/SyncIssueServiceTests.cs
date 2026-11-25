using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Sync;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Sync;

namespace HaulScout.Tests;

public sealed class SyncIssueServiceTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org, SyncIssueService Service) Harness(string name)
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(name).Options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Org" });
        db.SaveChanges();
        SyncIssueService service = new(new EfSyncIssueStore(db), null!, new StubCurrentUser(org, Guid.NewGuid()), new FakeTimeProvider(DateTimeOffset.UtcNow));
        return (db, org, service);
    }

    private static SyncRun Run(Guid org, string platform) =>
        new() { OrganizationId = org, FormatKey = "agent", Platform = platform, StartedAt = DateTimeOffset.UtcNow };

    private static SyncRunIssue Issue(Guid org, SyncRun run, string? code, string reference, SyncIssueSeverity severity = SyncIssueSeverity.Warning) =>
        new() { OrganizationId = org, SyncRun = run, Code = code, ExternalReference = reference, Message = "m", Severity = severity, Snapshot = "<html/>" };

    [Fact]
    public async Task Open_issues_group_by_marketplace_and_kind_with_a_title_and_fix()
    {
        (HaulScoutDbContext db, Guid org, SyncIssueService service) = Harness(nameof(Open_issues_group_by_marketplace_and_kind_with_a_title_and_fix));
        SyncRun posh = Run(org, "Poshmark");
        SyncRun ebay = Run(org, "eBay");
        db.SyncRunIssues.AddRange(
            Issue(org, posh, SyncIssueCodes.FieldMissing, "PM-1"),
            Issue(org, posh, SyncIssueCodes.FieldMissing, "PM-2"),
            Issue(org, ebay, SyncIssueCodes.Rejected, "EB-1", SyncIssueSeverity.Error),
            new SyncRunIssue { OrganizationId = org, SyncRun = ebay, Message = "done", State = SyncIssueState.Resolved });
        await db.SaveChangesAsync();

        IReadOnlyList<SyncIssueGroupDto> groups = (await service.ListOpenAsync(CancellationToken.None)).Value!;

        Assert.Equal(2, groups.Count);
        Assert.Equal("eBay", groups[0].Platform);
        SyncIssueGroupDto missing = groups.Single(group => group.Code == SyncIssueCodes.FieldMissing);
        Assert.Equal(2, missing.Count);
        Assert.Equal("Detail missing from a record", missing.Title);
        Assert.All(missing.Issues, issue => Assert.True(issue.HasSnapshot));
    }

    [Fact]
    public async Task Resolving_a_group_closes_every_open_issue_of_that_kind_on_that_marketplace_only()
    {
        (HaulScoutDbContext db, Guid org, SyncIssueService service) = Harness(nameof(Resolving_a_group_closes_every_open_issue_of_that_kind_on_that_marketplace_only));
        SyncRun posh = Run(org, "Poshmark");
        SyncRun ebay = Run(org, "eBay");
        db.SyncRunIssues.AddRange(
            Issue(org, posh, SyncIssueCodes.PhotoFetch, "1"),
            Issue(org, posh, SyncIssueCodes.PhotoFetch, "2"),
            Issue(org, ebay, SyncIssueCodes.PhotoFetch, "3"));
        await db.SaveChangesAsync();

        AppResult<int> closed = await service.ResolveGroupAsync(new ResolveSyncIssueGroupRequest("poshmark", SyncIssueCodes.PhotoFetch, "fixed"), CancellationToken.None);

        Assert.Equal(2, closed.Value);
        Assert.Equal(1, await db.SyncRunIssues.CountAsync(issue => issue.State == SyncIssueState.Open));
        Assert.All(await db.SyncRunIssues.Where(issue => issue.State == SyncIssueState.Resolved).ToListAsync(), issue => Assert.Equal("fixed", issue.Resolution));
    }

    [Fact]
    public async Task One_issue_can_be_dismissed_with_who_and_when()
    {
        (HaulScoutDbContext db, Guid org, SyncIssueService service) = Harness(nameof(One_issue_can_be_dismissed_with_who_and_when));
        SyncRunIssue issue = Issue(org, Run(org, "Vinted"), SyncIssueCodes.SiteChanged, "V-1");
        db.SyncRunIssues.Add(issue);
        await db.SaveChangesAsync();

        await service.ResolveAsync(issue.Id, new ResolveSyncIssueRequest(Dismiss: true), CancellationToken.None);

        SyncRunIssue stored = await db.SyncRunIssues.SingleAsync();
        Assert.Equal(SyncIssueState.Dismissed, stored.State);
        Assert.NotNull(stored.ResolvedAt);
        Assert.NotNull(stored.ResolvedByUserId);
    }
}
