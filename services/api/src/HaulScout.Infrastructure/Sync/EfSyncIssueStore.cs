using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Sync;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Sync;

public sealed class EfSyncIssueStore(HaulScoutDbContext db) : ISyncIssueStore
{
    public async Task<IReadOnlyList<SyncIssueRow>> ListOpenAsync(Guid organizationId, int take, CancellationToken cancellationToken) =>
        await db.SyncRunIssues.AsNoTracking()
            .Where(issue => issue.OrganizationId == organizationId && issue.State == SyncIssueState.Open && issue.SyncRun != null)
            .OrderByDescending(issue => issue.CreatedAt)
            .Take(take)
            .Select(issue => new SyncIssueRow(
                issue,
                issue.SyncRun!.Platform,
                issue.SyncRun.ChannelAccountId,
                issue.SyncRun.ChannelAccount != null ? issue.SyncRun.ChannelAccount.DisplayName : null,
                issue.SyncRun.Loop))
            .ToListAsync(cancellationToken);

    public Task<SyncRunIssue?> FindAsync(Guid organizationId, Guid issueId, CancellationToken cancellationToken) =>
        db.SyncRunIssues.Include(issue => issue.SyncRun).FirstOrDefaultAsync(issue => issue.OrganizationId == organizationId && issue.Id == issueId, cancellationToken);

    public async Task<IReadOnlyList<SyncRunIssue>> ListOpenInGroupAsync(Guid organizationId, string platform, string? code, CancellationToken cancellationToken)
    {
        string lowered = platform.ToLower();
        return await db.SyncRunIssues
            .Where(issue => issue.OrganizationId == organizationId
                && issue.State == SyncIssueState.Open
                && issue.Code == code
                && issue.SyncRun != null
                && issue.SyncRun.Platform.ToLower() == lowered)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
