using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>
/// A browser paired to the organization to sync its marketplace accounts — the extension, running
/// in a browser where the operator is signed in to eBay, Depop and the rest.
///
/// It authenticates with a device token that can only sync: report what it sees, take a lease,
/// deliver what it read. Only the token's hash is stored. The agent acts as the person who paired
/// it, with that person's current permissions, so removing someone from the organization — or
/// revoking the browser — stops it at once.
/// </summary>
public sealed class SyncAgent : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }

    /// <summary>What the operator calls it — "Office mini-PC". Starts as the browser and system.</summary>
    public required string Name { get; set; }

    public required string Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public required string ExtensionVersion { get; set; }

    public Guid PairedByUserId { get; set; }
    public AppUser? PairedByUser { get; set; }
    public DateTimeOffset PairedAt { get; set; }

    /// <summary>SHA-256 of the device token, hex.</summary>
    public required string TokenHash { get; set; }
    public DateTimeOffset TokenIssuedAt { get; set; }

    /// <summary>
    /// The token before the last rotation, still accepted until the agent is seen using the new one —
    /// a rotation whose response never arrived must not lock the browser out.
    /// </summary>
    public string? PreviousTokenHash { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }

    public ICollection<SyncAgentMarketplace> Marketplaces { get; set; } = [];
}

/// <summary>
/// What a paired browser last reported about one marketplace: whether it may reach it, who it is
/// signed in as, and whether its safety governor has paused it.
/// </summary>
public sealed class SyncAgentMarketplace : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid SyncAgentId { get; set; }
    public SyncAgent? SyncAgent { get; set; }

    /// <summary>SalesChannelCatalog value, e.g. "eBay".</summary>
    public required string Platform { get; set; }

    /// <summary>Whether the operator has let the extension reach this marketplace in this browser.</summary>
    public bool AccessGranted { get; set; }

    /// <summary>Null when the browser could not tell.</summary>
    public bool? SignedIn { get; set; }

    /// <summary>The marketplace username the browser is signed in as.</summary>
    public string? Username { get; set; }

    /// <summary>The marketplace's own stable id for that user, where it exposes one.</summary>
    public string? MarketplaceUserId { get; set; }

    /// <summary>Set while the agent's governor holds this marketplace back — after a challenge page or rate limit.</summary>
    public DateTimeOffset? PausedUntil { get; set; }
    public string? PauseReason { get; set; }

    public DateTimeOffset ObservedAt { get; set; }
}

/// <summary>
/// A one-time code shown in the portal to pair a browser. Only its hash is stored; it works once,
/// for a few minutes, and pairs the browser as the person who asked for it.
/// </summary>
public sealed class SyncPairing : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public required string CodeHash { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? SyncAgentId { get; set; }
}

/// <summary>
/// One recurring job on one account — "eBay sales" for the eBay store — with its lease and where it
/// got to.
///
/// The lease is what lets several browsers be paired safely: before scanning, an agent takes the
/// lease, so the same account is never scanned twice at once. It expires on its own, so a laptop
/// closed mid-scan hands the work over instead of holding it.
/// </summary>
public sealed class SyncLoop : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid ChannelAccountId { get; set; }
    public ChannelAccount? ChannelAccount { get; set; }

    public SyncLoopKind Kind { get; set; }

    public Guid? LeaseAgentId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>The run the current lease is for.</summary>
    public Guid? LeaseRunId { get; set; }

    /// <summary>Where the last successful run stopped, in the adapter's own terms.</summary>
    public string? Cursor { get; set; }

    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastFinishedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public Guid? LastRunId { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }

    /// <summary>Changes on every write, so two agents racing for the lease cannot both win.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public bool IsLeasedBy(Guid agentId, DateTimeOffset now) =>
        LeaseAgentId == agentId && LeaseExpiresAt > now;

    public bool IsLeasedByAnother(Guid agentId, DateTimeOffset now) =>
        LeaseAgentId is Guid holder && holder != agentId && LeaseExpiresAt > now;
}

/// <summary>
/// A person asking for an account to be scanned now — "scan eBay sales". The agent holding the
/// account's lease runs it; the request shows its life on the hub so a click never vanishes.
/// Requests for the same account and scope coalesce into one.
/// </summary>
public sealed class SyncScanRequest : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid ChannelAccountId { get; set; }
    public ChannelAccount? ChannelAccount { get; set; }

    public SyncLoopKind Loop { get; set; }

    /// <summary>Set for a history backfill: read sales from this date, not from where the last scan stopped.</summary>
    public DateTimeOffset? From { get; set; }

    public SyncScanStatus Status { get; set; } = SyncScanStatus.Queued;
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }

    public Guid? SyncAgentId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public Guid? SyncRunId { get; set; }

    /// <summary>The outcome in the operator's words — the run's summary, or why it did not run.</summary>
    public string? Message { get; set; }

    public bool IsOpen => Status is SyncScanStatus.Queued or SyncScanStatus.Running;
}

/// <summary>The recurring jobs an agent runs per account.</summary>
public enum SyncLoopKind
{
    /// <summary>New sales since the last scan — the double-sell guard.</summary>
    Sales,

    /// <summary>Every live listing: id, title, price, quantity.</summary>
    Listings,
}

/// <summary>Why a run happened.</summary>
public enum SyncTrigger
{
    /// <summary>The account's schedule said it was due.</summary>
    Scheduled,

    /// <summary>Someone asked for it from the portal.</summary>
    Requested,

    /// <summary>Someone asked for it from the extension itself.</summary>
    Extension,
}

public enum SyncScanStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,

    /// <summary>No browser picked it up in time.</summary>
    Expired,
}

/// <summary>Why an agent could not finish a run, so the hub can say what to do about it.</summary>
public enum SyncFailureKind
{
    /// <summary>The browser is not signed in to the marketplace.</summary>
    SignedOut,

    /// <summary>The browser is signed in as a different marketplace account than the one bound.</summary>
    WrongAccount,

    /// <summary>The marketplace asked the agent to slow down.</summary>
    RateLimited,

    /// <summary>The marketplace showed a challenge page — a captcha or a security check.</summary>
    Challenge,

    /// <summary>The marketplace's pages no longer look the way the adapter expects.</summary>
    SiteChanged,

    /// <summary>The marketplace or the network could not be reached.</summary>
    Network,

    /// <summary>The browser has not allowed the extension to reach the marketplace.</summary>
    NoAccess,

    /// <summary>The sync tab was closed, or left the marketplace, before the work finished.</summary>
    Interrupted,

    /// <summary>Anything else; the message says what.</summary>
    Error,
}
