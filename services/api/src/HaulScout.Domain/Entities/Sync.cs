using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// One arrival of marketplace data, and what became of it.
///
/// Every manual file drop — and, from the sync agent, every scan — is recorded here: what it was,
/// when, which account, how many records it created, changed or left alone, and which rows it could
/// not use. Imports used to leave no trace beyond their effects, so "did I already import July?" and
/// "why is this order missing?" had no answer. The raw file is kept alongside, so a parser fix can
/// re-run history instead of asking for the export again.
/// </summary>
public sealed class SyncRun : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? ChannelAccountId { get; set; }
    public ChannelAccount? ChannelAccount { get; set; }

    public SyncSource Source { get; set; }

    /// <summary>The recognised format, e.g. <c>depop.sales</c> — stable, versioned by the catalog.</summary>
    public required string FormatKey { get; set; }
    public required string Platform { get; set; }
    public SyncRunStatus Status { get; set; } = SyncRunStatus.Running;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Rejected { get; set; }

    /// <summary>The period the data covers, where the format says — "Jul 1–31".</summary>
    public DateTimeOffset? CoversFrom { get; set; }
    public DateTimeOffset? CoversTo { get; set; }

    /// <summary>
    /// True when the data is a complete picture of its subject as of <see cref="CoversTo"/> — an
    /// "all available listings" export — so anything previously seen and now absent is no longer live.
    /// </summary>
    public bool IsCompleteSnapshot { get; set; }

    /// <summary>One line on the outcome, in the operator's language.</summary>
    public string? Summary { get; set; }

    public Guid? TriggeredByUserId { get; set; }
    public Guid? ArtifactId { get; set; }
    public SyncArtifact? Artifact { get; set; }

    /// <summary>Set when this run re-processed an earlier run's file.</summary>
    public Guid? RerunOfId { get; set; }

    public ICollection<SyncRunIssue> Issues { get; set; } = [];
}

/// <summary>A row or record a run could not use, or used with a caveat.</summary>
public sealed class SyncRunIssue : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid SyncRunId { get; set; }
    public SyncRun? SyncRun { get; set; }

    public SyncIssueSeverity Severity { get; set; }

    /// <summary>1-based row in the source file, when the issue belongs to one.</summary>
    public int? RowNumber { get; set; }

    /// <summary>The platform's own reference for the record — an order or listing id — when known.</summary>
    public string? ExternalReference { get; set; }

    public required string Message { get; set; }
}

/// <summary>
/// The exact bytes a run read, compressed. Kept so a run can be repeated after a parser fix, and so
/// "what did this export actually say?" can always be answered.
/// </summary>
public sealed class SyncArtifact : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the original bytes, hex — how "this exact file was imported before" is spotted.</summary>
    public required string Sha256 { get; set; }

    /// <summary>Gzip-compressed original bytes.</summary>
    public required byte[] CompressedContent { get; set; }
}

public enum SyncSource
{
    File,
    Agent,
}

public enum SyncRunStatus
{
    Running,
    Succeeded,

    /// <summary>Finished, and some rows were rejected or carry warnings.</summary>
    CompletedWithIssues,

    Failed,
}

public enum SyncIssueSeverity
{
    Warning,
    Error,
}
