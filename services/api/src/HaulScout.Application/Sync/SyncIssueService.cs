using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Sync.Agents;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync;

/// <summary>
/// Sync issues as work: grouped by marketplace and kind, each open until a person resolves it or a later scan proves
/// it gone. A retry asks the agent to scan that account again; the issue closes when the scan shows it fixed.
/// </summary>
public sealed class SyncIssueService(ISyncIssueStore store, SyncHubService hub, ICurrentUserContext currentUser, TimeProvider clock)
{
    private const int OpenLimit = 500;

    public async Task<AppResult<IReadOnlyList<SyncIssueGroupDto>>> ListOpenAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SyncIssueRow> rows = await store.ListOpenAsync(currentUser.OrganizationId, OpenLimit, cancellationToken);
        SyncIssueGroupDto[] groups =
        [
            .. rows
                .GroupBy(row => (Platform: row.Platform, Code: row.Issue.Code ?? SyncIssueCodes.Other))
                .Select(group =>
                {
                    (string title, string? fix) = SyncIssueCodes.Describe(group.Key.Code);
                    SyncIssueSeverity severity = group.Any(row => row.Issue.Severity == SyncIssueSeverity.Error) ? SyncIssueSeverity.Error : SyncIssueSeverity.Warning;
                    return new SyncIssueGroupDto(
                        group.Key.Platform,
                        group.Key.Code,
                        title,
                        fix,
                        severity,
                        group.Count(),
                        group.Max(row => row.Issue.CreatedAt),
                        [.. group.OrderByDescending(row => row.Issue.CreatedAt).Select(ToDto)]);
                })
                .OrderByDescending(group => group.Severity)
                .ThenByDescending(group => group.LatestAt),
        ];
        return AppResult<IReadOnlyList<SyncIssueGroupDto>>.Ok(groups);
    }

    public async Task<AppResult<SyncIssueSnapshotDto>> GetSnapshotAsync(Guid issueId, CancellationToken cancellationToken)
    {
        SyncRunIssue? issue = await store.FindAsync(currentUser.OrganizationId, issueId, cancellationToken);
        return issue is null
            ? AppResult<SyncIssueSnapshotDto>.NotFound("Issue not found.")
            : AppResult<SyncIssueSnapshotDto>.Ok(new SyncIssueSnapshotDto(issue.Id, issue.Detail, issue.Snapshot));
    }

    public async Task<AppResult> ResolveAsync(Guid issueId, ResolveSyncIssueRequest request, CancellationToken cancellationToken)
    {
        SyncRunIssue? issue = await store.FindAsync(currentUser.OrganizationId, issueId, cancellationToken);
        if (issue is null)
        {
            return AppResult.NotFound("Issue not found.");
        }

        Close(issue, request.Dismiss ? SyncIssueState.Dismissed : SyncIssueState.Resolved, request.Note);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult<int>> ResolveGroupAsync(ResolveSyncIssueGroupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Platform))
        {
            return AppResult<int>.Validation("Name the marketplace.");
        }

        string? code = request.Code == SyncIssueCodes.Other ? null : request.Code;
        IReadOnlyList<SyncRunIssue> issues = await store.ListOpenInGroupAsync(currentUser.OrganizationId, request.Platform, code, cancellationToken);
        foreach (SyncRunIssue issue in issues)
        {
            Close(issue, request.Dismiss ? SyncIssueState.Dismissed : SyncIssueState.Resolved, request.Note);
        }

        await store.SaveChangesAsync(cancellationToken);
        return AppResult<int>.Ok(issues.Count);
    }

    /// <summary>Asks for a fresh scan of the account the issue came from. The issue stays open until that scan shows it gone.</summary>
    public async Task<AppResult<RequestScanResultDto>> RetryAsync(Guid issueId, CancellationToken cancellationToken)
    {
        SyncRunIssue? issue = await store.FindAsync(currentUser.OrganizationId, issueId, cancellationToken);
        if (issue?.SyncRun is not SyncRun run)
        {
            return AppResult<RequestScanResultDto>.NotFound("Issue not found.");
        }

        if (run.ChannelAccountId is not Guid accountId)
        {
            return AppResult<RequestScanResultDto>.Validation("This issue came from a file import. Import a corrected file instead.");
        }

        SyncScanScope scope = run.Loop == SyncLoopKind.Listings ? SyncScanScope.Listings : SyncScanScope.Sales;
        AppResult<RequestScanResultDto> result = await hub.RequestScanAsync(new RequestScanRequest(scope, accountId), cancellationToken);
        if (result.Status == AppResultStatus.Ok)
        {
            issue.Resolution = $"Retry requested {clock.GetUtcNow():yyyy-MM-dd HH:mm} UTC.";
            await store.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private void Close(SyncRunIssue issue, SyncIssueState state, string? note)
    {
        issue.State = state;
        issue.ResolvedAt = clock.GetUtcNow();
        issue.ResolvedByUserId = currentUser.UserId;
        issue.Resolution = Text.Fit(Text.TrimToNull(note), 500) ?? (state == SyncIssueState.Dismissed ? "Dismissed." : "Resolved.");
    }

    private static SyncIssueDto ToDto(SyncIssueRow row) => new(
        row.Issue.Id,
        row.Issue.SyncRunId,
        row.AccountName,
        row.Loop,
        row.Issue.Severity,
        row.Issue.ExternalReference,
        row.Issue.Message,
        row.Issue.Step,
        row.Issue.Attempts,
        row.Issue.Detail,
        row.Issue.Snapshot is not null,
        row.Issue.Fix ?? SyncIssueCodes.Describe(row.Issue.Code).Fix,
        row.Issue.Resolution,
        row.Issue.CreatedAt);
}
