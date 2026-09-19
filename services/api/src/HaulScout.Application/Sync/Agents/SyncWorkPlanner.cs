using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync.Agents;

/// <summary>
/// Decides what one browser should run now. Pure: the same facts always give the same plan, so the
/// rules are tested without a browser, a clock or a database.
///
/// A browser is offered an account's work only when it is signed in to that marketplace as the
/// account's bound user and has not paused it; another browser's live lease keeps it out. Work a
/// person asked for comes first. Scheduled work falls due one interval after the loop last started,
/// and backs off after failures so a broken loop does not retry at full speed all day.
/// </summary>
public static class SyncWorkPlanner
{
    /// <summary>Scheduled retries after failures stretch to at most this many intervals.</summary>
    public const int MaxBackoffIntervals = 8;

    public static IReadOnlyList<AgentWorkDto> Plan(
        DateTimeOffset now,
        Guid agentId,
        string extensionVersion,
        IReadOnlyList<ChannelAccount> accounts,
        IReadOnlyList<SyncLoop> loops,
        IReadOnlyList<SyncScanRequest> openRequests,
        IReadOnlyList<SyncAgentMarketplace> agentMarketplaces,
        SyncAgentOptions options)
    {
        List<(AgentWorkDto Work, DateTimeOffset DueAt)> requested = [];
        List<(AgentWorkDto Work, DateTimeOffset DueAt)> scheduled = [];

        foreach (ChannelAccount account in accounts)
        {
            if (!account.IsActive || account.IsNativelySynced || !account.IsBound
                || SyncPlatforms.Find(account.Channel) is not SyncPlatform platform
                || !options.For(platform.Platform).AllowsVersion(extensionVersion)
                || !CanServe(agentMarketplaces, platform.Platform, account, now))
            {
                continue;
            }

            foreach (SyncLoopKind kind in platform.Loops)
            {
                SyncLoop? loop = loops.FirstOrDefault(candidate => candidate.ChannelAccountId == account.Id && candidate.Kind == kind);
                if (loop is not null && loop.LeaseExpiresAt > now && loop.LeaseAgentId is not null)
                {
                    // Running here or elsewhere; either way, not again now.
                    continue;
                }

                SyncScanRequest? request = openRequests
                    .Where(candidate => candidate.ChannelAccountId == account.Id && candidate.Loop == kind && candidate.Status == SyncScanStatus.Queued)
                    .OrderBy(candidate => candidate.RequestedAt)
                    .FirstOrDefault();
                if (request is not null)
                {
                    requested.Add((new AgentWorkDto(account.Id, platform.Platform, kind, SyncTrigger.Requested, request.Id, request.From), request.RequestedAt));
                    continue;
                }

                if (DueAt(account.EveryMinutes(kind), loop) is DateTimeOffset dueAt && dueAt <= now)
                {
                    scheduled.Add((new AgentWorkDto(account.Id, platform.Platform, kind, SyncTrigger.Scheduled, null, null), dueAt));
                }
            }
        }

        return
        [
            .. requested.OrderBy(item => item.DueAt).Select(item => item.Work),
            .. scheduled.OrderBy(item => item.DueAt).Select(item => item.Work),
        ];
    }

    /// <summary>
    /// When a scheduled loop next falls due; null when its schedule is off. A loop that never ran is due
    /// now. After failures the wait doubles per failure, up to <see cref="MaxBackoffIntervals"/> intervals.
    /// </summary>
    public static DateTimeOffset? DueAt(int? everyMinutes, SyncLoop? loop)
    {
        if (everyMinutes is not int minutes || minutes <= 0)
        {
            return null;
        }

        if (loop?.LastStartedAt is not DateTimeOffset lastStarted)
        {
            return DateTimeOffset.MinValue;
        }

        int intervals = loop.ConsecutiveFailures <= 0
            ? 1
            : Math.Min(MaxBackoffIntervals, 1 << Math.Min(loop.ConsecutiveFailures, 3));
        return lastStarted.AddMinutes(minutes * intervals);
    }

    /// <summary>Signed in to the platform as the account's bound user, with access, and not paused.</summary>
    public static bool CanServe(IEnumerable<SyncAgentMarketplace> marketplaces, string platform, ChannelAccount account, DateTimeOffset now) =>
        marketplaces.Any(marketplace =>
            string.Equals(marketplace.Platform, platform, StringComparison.OrdinalIgnoreCase)
            && marketplace.AccessGranted
            && marketplace.SignedIn == true
            && SameMarketplaceUser(marketplace.Username, account.BoundUsername)
            && !(marketplace.PausedUntil > now));

    /// <summary>Marketplace usernames are not case-sensitive on any supported marketplace.</summary>
    public static bool SameMarketplaceUser(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
