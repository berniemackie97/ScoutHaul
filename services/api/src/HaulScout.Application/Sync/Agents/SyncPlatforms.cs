using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync.Agents;

/// <summary>
/// A marketplace the sync agent has an adapter for: which loops it can run, how far back its history
/// reaches, the cadences the hub suggests and the least it allows, and how gently the agent must
/// treat the site.
/// </summary>
public sealed record SyncPlatform(
    string Platform,
    IReadOnlyList<SyncLoopKind> Loops,
    int HistoryDays,
    int SuggestedSalesMinutes,
    int MinSalesMinutes,
    int SuggestedListingsMinutes,
    int MinListingsMinutes,
    int MinSecondsBetweenRequests,
    int MaxRequestsPerMinute,
    int MaxRequestsPerDay)
{
    public bool Supports(SyncLoopKind loop) => Loops.Contains(loop);

    public int MinMinutes(SyncLoopKind loop) => loop == SyncLoopKind.Sales ? MinSalesMinutes : MinListingsMinutes;
}

/// <summary>
/// The marketplaces with adapters. A platform joins this list in the same change that ships its
/// adapter in the extension; until then its accounts show as not yet supported, rather than as
/// something that silently never syncs.
/// </summary>
public static class SyncPlatforms
{
    /// <summary>
    /// eBay. Sales come from Seller Hub's order list with fees from the seller's own transaction
    /// ledger, which reaches back 90 days; listings from Manage active listings.
    /// </summary>
    public static readonly SyncPlatform Ebay = new(
        "eBay",
        [SyncLoopKind.Sales, SyncLoopKind.Listings],
        HistoryDays: 90,
        SuggestedSalesMinutes: 10,
        MinSalesMinutes: 5,
        SuggestedListingsMinutes: 240,
        MinListingsMinutes: 60,
        MinSecondsBetweenRequests: 3,
        MaxRequestsPerMinute: 12,
        MaxRequestsPerDay: 1500);

    public static readonly IReadOnlyList<SyncPlatform> All = [Ebay];

    public static SyncPlatform? Find(string? platform) =>
        All.FirstOrDefault(candidate => string.Equals(candidate.Platform, platform?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Operator-level switches for the sync agent, from configuration (<c>Sync:Agent</c>). The per-platform
/// <see cref="SyncPlatformSwitch.Enabled"/> is the remote kill switch: when a marketplace changes its
/// site and an adapter starts misreading it, turning it off stops every browser on the next heartbeat,
/// without anyone reinstalling anything.
/// </summary>
public sealed class SyncAgentOptions
{
    public const string SectionName = "Sync:Agent";

    public int HeartbeatSeconds { get; set; } = 60;

    /// <summary>How long a lease lasts without being renewed. Each batch renews it.</summary>
    public int LeaseMinutes { get; set; } = 5;

    public int PairingCodeMinutes { get; set; } = 10;

    public int TokenRotationDays { get; set; } = 7;

    /// <summary>A browser unseen for this long must be paired again; its token no longer works.</summary>
    public int InactiveAgentDays { get; set; } = 30;

    /// <summary>A browser seen within this long counts as online.</summary>
    public int OnlineWithinSeconds { get; set; } = 180;

    /// <summary>A scan request nobody picks up in this long expires, and says so.</summary>
    public int ScanRequestExpiryHours { get; set; } = 24;

    public Dictionary<string, SyncPlatformSwitch> Platforms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public SyncPlatformSwitch For(string platform) =>
        Platforms.TryGetValue(platform, out SyncPlatformSwitch? configured) ? configured : SyncPlatformSwitch.On;
}

public sealed class SyncPlatformSwitch
{
    public static readonly SyncPlatformSwitch On = new();

    public bool Enabled { get; set; } = true;

    /// <summary>Shown to the operator when <see cref="Enabled"/> is false — "eBay changed its orders page; a fix is on the way."</summary>
    public string? DisabledReason { get; set; }

    /// <summary>Extension versions whose adapter for this platform must not run — switched off without switching off newer ones.</summary>
    public List<string> DisabledAdapterVersions { get; set; } = [];

    public bool AllowsVersion(string? extensionVersion) =>
        Enabled && (extensionVersion is null || !DisabledAdapterVersions.Contains(extensionVersion, StringComparer.OrdinalIgnoreCase));
}
