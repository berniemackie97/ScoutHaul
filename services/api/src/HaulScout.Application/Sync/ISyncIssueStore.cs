using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync;

/// <summary>An issue with the run facts it is grouped and shown by.</summary>
public sealed record SyncIssueRow(SyncRunIssue Issue, string Platform, Guid? ChannelAccountId, string? AccountName, SyncLoopKind? Loop);

public interface ISyncIssueStore
{
    Task<IReadOnlyList<SyncIssueRow>> ListOpenAsync(Guid organizationId, int take, CancellationToken cancellationToken);

    /// <summary>Tracked, with its run.</summary>
    Task<SyncRunIssue?> FindAsync(Guid organizationId, Guid issueId, CancellationToken cancellationToken);

    /// <summary>Open issues of one kind on one platform, tracked.</summary>
    Task<IReadOnlyList<SyncRunIssue>> ListOpenInGroupAsync(Guid organizationId, string platform, string? code, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
