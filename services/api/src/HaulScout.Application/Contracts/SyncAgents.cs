using HaulScout.Domain.Entities;

namespace HaulScout.Application.Contracts;

// The sync agent's protocol (the browser extension ↔ the API) and the portal's view of it.

// ── Agent protocol ─────────────────────────────────────────────────────────────────────────────

/// <summary>A browser exchanging the one-time code the portal showed for its device token.</summary>
public sealed record PairAgentRequest(
    string Code,
    string Browser,
    string ExtensionVersion,
    string? OperatingSystem = null,
    string? Name = null);

/// <summary>The device token is returned once, here, and never again.</summary>
public sealed record PairAgentResultDto(Guid AgentId, string AgentName, string Token, string OrganizationName);

/// <summary>What the browser can see of one marketplace right now.</summary>
public sealed record AgentMarketplaceReport(
    string Platform,
    bool AccessGranted,
    bool? SignedIn = null,
    string? Username = null,
    string? MarketplaceUserId = null,
    DateTimeOffset? PausedUntil = null,
    string? PauseReason = null);

public sealed record AgentHeartbeatRequest(
    string ExtensionVersion,
    string Browser,
    IReadOnlyList<AgentMarketplaceReport> Marketplaces,
    string? OperatingSystem = null);

/// <summary>How hard the agent may press one marketplace, and whether it may at all.</summary>
public sealed record AgentPlatformPolicyDto(
    string Platform,
    bool Enabled,
    string? DisabledReason,
    IReadOnlyList<SyncLoopKind> Loops,
    int MinSecondsBetweenRequests,
    int MaxRequestsPerMinute,
    int MaxRequestsPerDay,
    int HistoryDays);

/// <summary>An account the agent may sync, as the popup shows it.</summary>
public sealed record AgentAccountDto(
    Guid ChannelAccountId,
    string Platform,
    string DisplayName,
    string? BoundUsername,
    int? SalesEveryMinutes,
    int? ListingsEveryMinutes,
    IReadOnlyList<AgentLoopDto> Loops);

/// <summary>
/// Where one loop on one account stands, whichever browser ran it last — so the popup shows the same
/// "last synced" and "next" as the portal rather than only what this browser remembers.
/// </summary>
public sealed record AgentLoopDto(
    SyncLoopKind Loop,
    int? EveryMinutes,
    DateTimeOffset? LastSucceededAt,
    string? LastError,
    DateTimeOffset? NextDueAt,
    bool Running);

/// <summary>One thing the agent should run now.</summary>
public sealed record AgentWorkDto(
    Guid ChannelAccountId,
    string Platform,
    SyncLoopKind Loop,
    SyncTrigger Trigger,
    Guid? ScanRequestId,
    DateTimeOffset? From);

public sealed record AgentHeartbeatDto(
    Guid AgentId,
    string AgentName,
    string OrganizationName,
    int HeartbeatSeconds,
    IReadOnlyList<AgentPlatformPolicyDto> Platforms,
    IReadOnlyList<AgentAccountDto> Accounts,
    IReadOnlyList<AgentWorkDto> Work,
    DateTimeOffset ServerTime,
    string? NewToken = null);

/// <summary>
/// The agent asking to run one loop on one account. <see cref="SignedInAs"/> is who the browser is
/// signed in as at this moment, checked against the account's binding before anything is read.
/// </summary>
public sealed record StartAgentRunRequest(
    Guid ChannelAccountId,
    SyncLoopKind Loop,
    SyncTrigger Trigger,
    string SignedInAs,
    Guid? ScanRequestId = null,
    string? MarketplaceUserId = null);

/// <summary>
/// A run the agent now holds the lease for. <see cref="Cursor"/> is where the last successful run
/// stopped; <see cref="From"/> is set for a history backfill.
/// </summary>
public sealed record AgentRunDto(Guid RunId, DateTimeOffset LeaseExpiresAt, string? Cursor, DateTimeOffset? From);

/// <summary>A record the agent could not use, or used with a caveat.</summary>
public sealed record AgentRunIssueInput(string Message, string? ExternalReference = null, bool Rejected = false);

/// <summary>One slice of what a run read. Sales runs carry orders; listing runs carry listings.</summary>
public sealed record AgentRunBatchRequest(
    IReadOnlyList<IngestPlatformOrderInput>? Orders = null,
    IReadOnlyList<IngestPlatformListingInput>? Listings = null,
    IReadOnlyList<AgentRunIssueInput>? Issues = null);

public sealed record AgentRunBatchResultDto(int Created, int Updated, int Unchanged, DateTimeOffset LeaseExpiresAt);

/// <param name="CompleteSnapshot">For a listings run: every live listing was read, so any listing seen before and now absent is no longer live.</param>
public sealed record CompleteAgentRunRequest(
    string? Cursor = null,
    DateTimeOffset? CoversFrom = null,
    DateTimeOffset? CoversTo = null,
    bool CompleteSnapshot = false);

/// <param name="Message">Detail beside the reason the kind already gives — an adapter's account of a page it could not read. Optional.</param>
public sealed record FailAgentRunRequest(SyncFailureKind Kind, string? Message = null);

/// <summary>
/// Work the agent was offered but could not start — the browser turned out to be signed out, or
/// signed in as someone else, before any run was opened. Reported so a scan someone asked for says
/// why it did not happen, and so the schedule backs off instead of offering it again every minute.
/// </summary>
public sealed record AgentWorkFailureRequest(
    Guid ChannelAccountId,
    SyncLoopKind Loop,
    SyncFailureKind Kind,
    string? Message = null,
    Guid? ScanRequestId = null);

// ── Portal ─────────────────────────────────────────────────────────────────────────────────────

/// <summary>What a person may ask to be scanned.</summary>
public enum SyncScanScope
{
    Sales,
    Listings,

    /// <summary>Sales from a date — the history the account's sales scans would otherwise not reach back to.</summary>
    History,

    /// <summary>Every loop the account's marketplace supports.</summary>
    Everything,
}

/// <param name="ChannelAccountId">One account; or null with <paramref name="Platform"/> for every account on it; or both null for all accounts.</param>
/// <param name="From">Required for <see cref="SyncScanScope.History"/>.</param>
public sealed record RequestScanRequest(SyncScanScope Scope, Guid? ChannelAccountId = null, string? Platform = null, DateTimeOffset? From = null);

public sealed record SyncScanRequestDto(
    Guid Id,
    Guid ChannelAccountId,
    string AccountName,
    string Platform,
    SyncLoopKind Loop,
    DateTimeOffset? From,
    SyncScanStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? RunningOn,
    Guid? RunId,
    string? Message);

/// <summary>An account a scan request left out, and why — so a click on "all accounts" says what it skipped.</summary>
public sealed record SkippedScanDto(Guid ChannelAccountId, string AccountName, string Reason);

public sealed record RequestScanResultDto(IReadOnlyList<SyncScanRequestDto> Requests, IReadOnlyList<SkippedScanDto> Skipped);

/// <summary>Null turns a loop off.</summary>
public sealed record UpdateSyncScheduleRequest(int? SalesEveryMinutes, int? ListingsEveryMinutes);

public sealed record BindSyncAccountRequest(string Username, string? MarketplaceUserId = null);

public sealed record RenameSyncAgentRequest(string Name);

public enum SyncAccountHealth
{
    /// <summary>A browser signed in as the bound account is online.</summary>
    Ready,

    /// <summary>The account is not bound to a marketplace account yet.</summary>
    NotBound,

    /// <summary>No online browser is signed in as the bound account.</summary>
    NoBrowser,

    /// <summary>Every browser that could sync it has paused this marketplace.</summary>
    Paused,

    /// <summary>Sync is switched off for this marketplace.</summary>
    Disabled,

    /// <summary>This marketplace has no sync adapter yet.</summary>
    Unsupported,
}

/// <summary>One loop on one account, as the hub shows it.</summary>
public sealed record SyncLoopDto(
    SyncLoopKind Loop,
    int? EveryMinutes,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastSucceededAt,
    Guid? LastRunId,
    string? LastError,
    int ConsecutiveFailures,
    string? RunningOn,
    DateTimeOffset? NextDueAt);

/// <summary>A browser that has reported a session on the account's marketplace.</summary>
public sealed record SyncSessionSeenDto(
    Guid AgentId,
    string AgentName,
    bool Online,
    bool AccessGranted,
    bool? SignedIn,
    string? Username,
    string? MarketplaceUserId,
    DateTimeOffset? PausedUntil,
    string? PauseReason);

public sealed record SyncAccountDto(
    Guid ChannelAccountId,
    string Platform,
    string DisplayName,
    bool Supported,
    IReadOnlyList<SyncLoopKind> SupportedLoops,
    bool SupportsHistory,
    string? BoundUsername,
    DateTimeOffset? BoundAt,
    SyncAccountHealth Health,
    string HealthMessage,
    IReadOnlyList<SyncLoopDto> Loops,
    IReadOnlyList<SyncSessionSeenDto> Sessions);

public sealed record SyncAgentMarketplaceDto(
    string Platform,
    bool AccessGranted,
    bool? SignedIn,
    string? Username,
    DateTimeOffset? PausedUntil,
    string? PauseReason,
    DateTimeOffset ObservedAt);

public sealed record SyncAgentDto(
    Guid Id,
    string Name,
    string Browser,
    string? OperatingSystem,
    string ExtensionVersion,
    DateTimeOffset PairedAt,
    string PairedBy,
    DateTimeOffset? LastSeenAt,
    bool Online,
    bool Revoked,
    IReadOnlyList<SyncAgentMarketplaceDto> Marketplaces);

/// <summary>The cadences the hub suggests beside each schedule, and the least it allows.</summary>
public sealed record SyncPlatformSettingsDto(
    string Platform,
    IReadOnlyList<SyncLoopKind> Loops,
    int SuggestedSalesMinutes,
    int MinSalesMinutes,
    int SuggestedListingsMinutes,
    int MinListingsMinutes,
    int HistoryDays,
    bool Enabled,
    string? DisabledReason);

public sealed record SyncOverviewDto(
    IReadOnlyList<SyncAccountDto> Accounts,
    IReadOnlyList<SyncAgentDto> Agents,
    IReadOnlyList<SyncScanRequestDto> Requests,
    IReadOnlyList<SyncPlatformSettingsDto> Platforms);

public sealed record CreateSyncPairingResultDto(Guid PairingId, string Code, DateTimeOffset ExpiresAt);

public enum SyncPairingStatus
{
    Waiting,
    Paired,
    Expired,
}

public sealed record SyncPairingDto(Guid PairingId, SyncPairingStatus Status, DateTimeOffset ExpiresAt, SyncAgentDto? Agent);
