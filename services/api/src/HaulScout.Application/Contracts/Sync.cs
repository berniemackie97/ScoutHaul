using HaulScout.Domain.Entities;

namespace HaulScout.Application.Contracts;

public sealed record SyncRunIssueDto(string Severity, int? RowNumber, string? ExternalReference, string Message);

/// <summary>
/// What a dropped file is, read but not yet imported — shown so the operator confirms before
/// anything changes.
/// </summary>
/// <param name="CanImport">False when the file is unrecognised or the caller's role cannot import this kind of report.</param>
/// <param name="Problem">Why it cannot be imported, in the operator's words.</param>
/// <param name="PreviouslyImportedAt">When this exact file was last imported, if it was.</param>
public sealed record ReportDetectionDto(
    string FileName,
    long SizeBytes,
    string? FormatKey,
    string? Platform,
    string? Label,
    int Records,
    DateTimeOffset? CoversFrom,
    DateTimeOffset? CoversTo,
    bool CompleteSnapshot,
    bool CanImport,
    string? Problem,
    IReadOnlyList<SyncRunIssueDto> Issues,
    DateTimeOffset? PreviouslyImportedAt);

public sealed record SyncRunDto(
    Guid Id,
    string FormatKey,
    string Platform,
    string? FormatLabel,
    string Source,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int Created,
    int Updated,
    int Unchanged,
    int Rejected,
    DateTimeOffset? CoversFrom,
    DateTimeOffset? CoversTo,
    bool IsCompleteSnapshot,
    string? Summary,
    string? FileName,
    int IssueCount,
    Guid? RerunOfId,
    bool CanRerun,
    string? Loop = null,
    string? Trigger = null,
    string? AgentName = null);

public sealed record SyncRunDetailDto(SyncRunDto Run, IReadOnlyList<SyncRunIssueDto> Issues);

/// <param name="QuietHidden">Scans that changed nothing, left out of this list; zero when they were asked for.</param>
public sealed record SyncRunListDto(IReadOnlyList<SyncRunDto> Items, int Total, int Page, int PageSize, int PageCount, int QuietHidden = 0);

/// <summary>One file's outcome: the run it produced, or why none was.</summary>
public sealed record ImportFileOutcomeDto(string FileName, SyncRunDto? Run, string? Problem);

public sealed record ImportResultDto(IReadOnlyList<ImportFileOutcomeDto> Files);

/// <summary>
/// How much is waiting for a person after imports, counted over everything rather than a page of it.
/// A count is null when the caller's role cannot act on that queue.
/// </summary>
public sealed record ImportQueuesDto(int? OrdersToMatch, int? ListingsToMatch);

public sealed record SyncIssueDto(
    Guid Id,
    Guid RunId,
    string? AccountName,
    SyncLoopKind? Loop,
    SyncIssueSeverity Severity,
    string? ExternalReference,
    string Message,
    string? Step,
    int Attempts,
    string? Detail,
    bool HasSnapshot,
    string? Fix,
    // Set while open only when a retry was asked for.
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record SyncIssueGroupDto(
    string Platform,
    string Code,
    string Title,
    string? Fix,
    SyncIssueSeverity Severity,
    int Count,
    DateTimeOffset LatestAt,
    IReadOnlyList<SyncIssueDto> Issues);

public sealed record SyncIssueSnapshotDto(Guid Id, string? Detail, string? Snapshot);

public sealed record ResolveSyncIssueRequest(string? Note = null, bool Dismiss = false);

public sealed record ResolveSyncIssueGroupRequest(string Platform, string Code, string? Note = null, bool Dismiss = false);
