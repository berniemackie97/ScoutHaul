using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Sync;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Sync;

public sealed class EfSyncRunStore(HaulScoutDbContext db) : ISyncRunStore
{
    public Task<SyncArtifact?> FindArtifactAsync(Guid organizationId, string sha256, CancellationToken cancellationToken) =>
        db.SyncArtifacts
            .Where(artifact => artifact.OrganizationId == organizationId && artifact.Sha256 == sha256)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<DateTimeOffset?> LastImportedAtAsync(Guid organizationId, string sha256, CancellationToken cancellationToken) =>
        db.SyncRuns
            .Where(run => run.OrganizationId == organizationId
                && run.Artifact != null
                && run.Artifact.Sha256 == sha256
                && run.Status != SyncRunStatus.Failed
                && run.Status != SyncRunStatus.Running)
            .MaxAsync(run => (DateTimeOffset?)run.FinishedAt, cancellationToken);

    public Task<SyncArtifact?> FindArtifactByIdAsync(Guid organizationId, Guid artifactId, CancellationToken cancellationToken) =>
        db.SyncArtifacts.SingleOrDefaultAsync(artifact => artifact.OrganizationId == organizationId && artifact.Id == artifactId, cancellationToken);

    public Task<SyncRun?> FindRunAsync(Guid organizationId, Guid runId, CancellationToken cancellationToken) =>
        db.SyncRuns
            .Include(run => run.SyncAgent)
            .SingleOrDefaultAsync(run => run.OrganizationId == organizationId && run.Id == runId, cancellationToken);

    public async Task<IReadOnlyList<SyncRunIssue>> ListIssuesAsync(Guid organizationId, Guid runId, CancellationToken cancellationToken) =>
        await db.SyncRunIssues
            .Where(issue => issue.OrganizationId == organizationId && issue.SyncRunId == runId)
            .OrderBy(issue => issue.RowNumber)
            .ThenBy(issue => issue.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<SyncRunRow> Rows, int Total, int QuietHidden)> ListRunsAsync(
        Guid organizationId, int skip, int take, bool includeQuiet, CancellationToken cancellationToken)
    {
        IQueryable<SyncRun> runs = db.SyncRuns.Where(run => run.OrganizationId == organizationId);
        // Quiet: a scan that succeeded without creating, changing or rejecting anything.
        IQueryable<SyncRun> quiet = runs.Where(run => run.Source == SyncSource.Agent
            && run.Status == SyncRunStatus.Succeeded
            && run.Created == 0 && run.Updated == 0 && run.Rejected == 0);
        int quietHidden = 0;
        if (!includeQuiet)
        {
            quietHidden = await quiet.CountAsync(cancellationToken);
            runs = runs.Where(run => !(run.Source == SyncSource.Agent
                && run.Status == SyncRunStatus.Succeeded
                && run.Created == 0 && run.Updated == 0 && run.Rejected == 0));
        }

        int total = await runs.CountAsync(cancellationToken);
        var rows = await runs
            .OrderByDescending(run => run.StartedAt)
            .Skip(skip)
            .Take(take)
            .Select(run => new
            {
                Run = run,
                FileName = run.Artifact == null ? null : run.Artifact.FileName,
                IssueCount = run.Issues.Count,
                AgentName = run.SyncAgent == null ? null : run.SyncAgent.Name,
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(row => new SyncRunRow(row.Run, row.FileName, row.IssueCount, row.AgentName)).ToList(), total, quietHidden);
    }

    public async Task<Guid?> SoleAccountAsync(Guid organizationId, string platform, CancellationToken cancellationToken)
    {
        List<Guid> accounts = await db.ChannelAccounts
            .Where(account => account.OrganizationId == organizationId && account.IsActive && account.Channel == platform)
            .Select(account => account.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        return accounts.Count == 1 ? accounts[0] : null;
    }

    public void Add(SyncArtifact artifact) => db.SyncArtifacts.Add(artifact);

    public void Add(SyncRun run) => db.SyncRuns.Add(run);

    public void AddIssues(IEnumerable<SyncRunIssue> issues) => db.SyncRunIssues.AddRange(issues);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task FailAsync(Guid organizationId, Guid runId, string summary, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        // Whatever the failed import left pending — possibly the very entities that made it fail — must
        // not ride along on the save that records the failure.
        db.ChangeTracker.Clear();

        SyncRun? run = await FindRunAsync(organizationId, runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        run.Status = SyncRunStatus.Failed;
        run.Summary = summary.Length > 500 ? summary[..497] + "…" : summary;
        run.FinishedAt = finishedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SyncRunIssue>> ListOpenIssuesForLoopAsync(
        Guid organizationId,
        Guid? channelAccountId,
        SyncLoopKind? loop,
        Guid exceptRunId,
        CancellationToken cancellationToken) =>
        await db.SyncRunIssues
            .Where(issue => issue.OrganizationId == organizationId
                && issue.State == SyncIssueState.Open
                && issue.SyncRunId != exceptRunId
                && issue.SyncRun != null
                && issue.SyncRun.ChannelAccountId == channelAccountId
                && issue.SyncRun.Loop == loop)
            .ToListAsync(cancellationToken);
}
