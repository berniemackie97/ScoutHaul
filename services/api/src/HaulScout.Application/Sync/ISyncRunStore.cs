using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync;

/// <summary>A run with the facts the history list shows beside it.</summary>
public sealed record SyncRunRow(SyncRun Run, string? FileName, int IssueCount, string? AgentName);

/// <summary>Sync runs' persistence port.</summary>
public interface ISyncRunStore
{
    /// <summary>The stored copy of a file with these exact bytes, if one was kept before.</summary>
    Task<SyncArtifact?> FindArtifactAsync(Guid organizationId, string sha256, CancellationToken cancellationToken);

    /// <summary>When a file with these exact bytes last finished importing, for "you imported this before".</summary>
    Task<DateTimeOffset?> LastImportedAtAsync(Guid organizationId, string sha256, CancellationToken cancellationToken);

    Task<SyncArtifact?> FindArtifactByIdAsync(Guid organizationId, Guid artifactId, CancellationToken cancellationToken);

    Task<SyncRun?> FindRunAsync(Guid organizationId, Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncRunIssue>> ListIssuesAsync(Guid organizationId, Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Newest first. Scans that changed nothing are left out unless <paramref name="includeQuiet"/> —
    /// a sales scan every ten minutes would otherwise bury every import that did something.
    /// </summary>
    Task<(IReadOnlyList<SyncRunRow> Rows, int Total, int QuietHidden)> ListRunsAsync(
        Guid organizationId, int skip, int take, bool includeQuiet, CancellationToken cancellationToken);

    /// <summary>The organization's account on this platform, when there is exactly one to attribute a file to.</summary>
    Task<Guid?> SoleAccountAsync(Guid organizationId, string platform, CancellationToken cancellationToken);

    void Add(SyncArtifact artifact);

    void Add(SyncRun run);

    void AddIssues(IEnumerable<SyncRunIssue> issues);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records a run as failed after an import threw. Anything the import left half-written is
    /// discarded first, so the failure itself can always be saved.
    /// </summary>
    Task FailAsync(Guid organizationId, Guid runId, string summary, DateTimeOffset finishedAt, CancellationToken cancellationToken);
}
