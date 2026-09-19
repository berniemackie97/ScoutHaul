using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using Microsoft.Extensions.Options;

namespace HaulScout.Application.Sync.Agents;

/// <summary>
/// The portal's side of sync: what every account's sync is doing, the schedules, which marketplace
/// account each one syncs, scans on demand, and the paired browsers.
///
/// Every automation here is the operator's to switch on. Schedules start off; binding is an explicit
/// confirmation of an account a browser has actually been seen signed in to; a scan runs because a
/// person asked, or because a schedule a person set came due.
/// </summary>
public sealed class SyncHubService(
    ISyncAgentStore store,
    ICurrentUserContext currentUser,
    IOrganizationAuthorizer access,
    TimeProvider clock,
    IOptions<SyncAgentOptions> optionsAccessor)
{
    /// <summary>
    /// Sales bring in money — prices, fees, payouts — so asking for them, or scheduling them, takes
    /// the finance permission as well as the listings one every sync action needs.
    /// </summary>
    private const string SalesNeedFinance = "Your role cannot bring in sales. Ask someone who manages finance.";

    /// <summary>Longest schedule the hub accepts: a week.</summary>
    public const int MaxEveryMinutes = 7 * 24 * 60;

    private readonly SyncAgentOptions options = optionsAccessor.Value;

    public async Task<SyncOverviewDto> OverviewAsync(CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyList<ChannelAccount> accounts = await store.ListAccountsAsync(organizationId, cancellationToken);
        IReadOnlyList<SyncLoop> loops = await store.ListLoopsAsync(organizationId, cancellationToken);
        IReadOnlyList<SyncAgent> agents = await store.ListAgentsAsync(organizationId, cancellationToken);
        IReadOnlyList<SyncScanRequest> requests = await store.ListScanRequestsAsync(organizationId, now.AddHours(-24), 30, cancellationToken);
        IReadOnlyDictionary<Guid, string> people = await store.UserNamesAsync(agents.Select(agent => agent.PairedByUserId), cancellationToken);

        return new SyncOverviewDto(
            accounts
                .Where(account => account.IsActive && !account.IsNativelySynced)
                .OrderBy(account => SyncPlatforms.Find(account.Channel) is null)
                .ThenBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(account => Account(account, loops, agents, now))
                .ToList(),
            agents.Select(agent => Agent(agent, people, now)).ToList(),
            requests.Select(request => Request(request, accounts, agents)).ToList(),
            SyncPlatforms.All.Select(Settings).ToList());
    }

    /// <summary>Sets how often each loop runs. Null switches a loop off, which is also where every account starts.</summary>
    public async Task<AppResult<SyncOverviewDto>> UpdateScheduleAsync(Guid accountId, UpdateSyncScheduleRequest request, CancellationToken cancellationToken)
    {
        ChannelAccount? account = await store.FindAccountAsync(currentUser.OrganizationId, accountId, cancellationToken);
        if (account is null)
        {
            return AppResult<SyncOverviewDto>.NotFound("That account was not found.");
        }

        if (SyncPlatforms.Find(account.Channel) is not SyncPlatform platform)
        {
            return AppResult<SyncOverviewDto>.Validation($"{account.Channel} cannot be synced from a browser yet.", AppErrorCode.SyncUnavailable);
        }

        foreach ((SyncLoopKind loop, int? minutes) in new[] { (SyncLoopKind.Sales, request.SalesEveryMinutes), (SyncLoopKind.Listings, request.ListingsEveryMinutes) })
        {
            if (minutes is not int every)
            {
                continue;
            }

            string what = loop == SyncLoopKind.Sales ? "sales" : "listings";
            if (!platform.Supports(loop))
            {
                return AppResult<SyncOverviewDto>.Validation($"{platform.Platform} {what} cannot be scanned yet.");
            }

            if (every < platform.MinMinutes(loop) || every > MaxEveryMinutes)
            {
                return AppResult<SyncOverviewDto>.Validation(
                    $"{platform.Platform} {what} can be scanned every {platform.MinMinutes(loop)} minutes at most often, and at least once a week.");
            }
        }

        if (request.SalesEveryMinutes != account.SalesScanEveryMinutes
            && !await access.HasPermission(currentUser.OrganizationId, HaulScoutPermissions.ManageFinance, cancellationToken))
        {
            return AppResult<SyncOverviewDto>.Validation(SalesNeedFinance);
        }

        account.SalesScanEveryMinutes = request.SalesEveryMinutes;
        account.ListingsScanEveryMinutes = request.ListingsEveryMinutes;
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SyncOverviewDto>.Ok(await OverviewAsync(cancellationToken));
    }

    /// <summary>
    /// Binds an account to the marketplace account a paired browser has been seen signed in as. Only a
    /// username a browser actually reported can be bound, so a typo can never point sync at a stranger.
    /// </summary>
    public async Task<AppResult<SyncOverviewDto>> BindAsync(Guid accountId, BindSyncAccountRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        ChannelAccount? account = await store.FindAccountAsync(organizationId, accountId, cancellationToken);
        if (account is null)
        {
            return AppResult<SyncOverviewDto>.NotFound("That account was not found.");
        }

        if (SyncPlatforms.Find(account.Channel) is not SyncPlatform platform)
        {
            return AppResult<SyncOverviewDto>.Validation($"{account.Channel} cannot be synced from a browser yet.", AppErrorCode.SyncUnavailable);
        }

        string? username = Text.TrimToNull(request.Username);
        IReadOnlyList<SyncAgent> agents = await store.ListAgentsAsync(organizationId, cancellationToken);
        SyncAgentMarketplace? seen = agents
            .Where(agent => agent.RevokedAt is null)
            .SelectMany(agent => agent.Marketplaces)
            .Where(marketplace => string.Equals(marketplace.Platform, platform.Platform, StringComparison.OrdinalIgnoreCase)
                && marketplace.SignedIn == true
                && SyncWorkPlanner.SameMarketplaceUser(marketplace.Username, username))
            .OrderByDescending(marketplace => marketplace.ObservedAt)
            .FirstOrDefault();
        if (seen is null)
        {
            return AppResult<SyncOverviewDto>.Validation(
                $"No paired browser has been seen signed in to {platform.Platform} as {username ?? "that account"}. Sign in to it in a paired browser first.");
        }

        IReadOnlyList<ChannelAccount> accounts = await store.ListAccountsAsync(organizationId, cancellationToken);
        if (accounts.FirstOrDefault(other => other.Id != account.Id
                && SyncPlatforms.Find(other.Channel) == platform
                && SyncWorkPlanner.SameMarketplaceUser(other.BoundUsername, seen.Username)) is ChannelAccount taken)
        {
            return AppResult<SyncOverviewDto>.Conflict($"{taken.DisplayName} already syncs {seen.Username}.");
        }

        account.BoundUsername = seen.Username;
        account.BoundMarketplaceUserId = seen.MarketplaceUserId;
        account.BoundAt = clock.GetUtcNow();
        account.BoundByUserId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(account.ExternalAccountId))
        {
            account.ExternalAccountId = seen.Username;
        }

        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SyncOverviewDto>.Ok(await OverviewAsync(cancellationToken));
    }

    /// <summary>Stops an account syncing anyone until it is bound again. Its waiting scans are cancelled with it.</summary>
    public async Task<AppResult<SyncOverviewDto>> UnbindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        ChannelAccount? account = await store.FindAccountAsync(organizationId, accountId, cancellationToken);
        if (account is null)
        {
            return AppResult<SyncOverviewDto>.NotFound("That account was not found.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        account.BoundUsername = null;
        account.BoundMarketplaceUserId = null;
        account.BoundAt = null;
        account.BoundByUserId = null;
        foreach (SyncScanRequest request in (await store.ListOpenScanRequestsAsync(organizationId, cancellationToken))
                     .Where(request => request.ChannelAccountId == account.Id && request.Status == SyncScanStatus.Queued))
        {
            SyncSweep.Finish(request, SyncScanStatus.Cancelled, "Cancelled: the account was unbound.", now);
        }

        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SyncOverviewDto>.Ok(await OverviewAsync(cancellationToken));
    }

    /// <summary>
    /// Asks for a scan now — of one account, a platform's accounts, or all of them. Requests for the
    /// same account and scope coalesce, so a burst of clicks queues one scan, and anything that cannot
    /// run is named with its reason rather than silently dropped.
    /// </summary>
    public async Task<AppResult<RequestScanResultDto>> RequestScanAsync(RequestScanRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        await SyncSweep.RunAsync(store, organizationId, now, options, cancellationToken);

        IReadOnlyList<ChannelAccount> accounts = await store.ListAccountsAsync(organizationId, cancellationToken);
        List<ChannelAccount> targets;
        if (request.ChannelAccountId is Guid accountId)
        {
            if (accounts.FirstOrDefault(account => account.Id == accountId) is not ChannelAccount one)
            {
                return AppResult<RequestScanResultDto>.NotFound("That account was not found.");
            }

            targets = [one];
        }
        else
        {
            string? platformFilter = Text.TrimToNull(request.Platform);
            targets = accounts
                .Where(account => account.IsActive && !account.IsNativelySynced)
                .Where(account => platformFilter is null || string.Equals(account.Channel, platformFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (request.Scope == SyncScanScope.History)
        {
            if (request.From is not DateTimeOffset from)
            {
                return AppResult<RequestScanResultDto>.Validation("Choose the date to read sales from.");
            }

            if (from > now)
            {
                return AppResult<RequestScanResultDto>.Validation("The date to read sales from is in the future.");
            }
        }

        IReadOnlyList<SyncScanRequest> open = await store.ListOpenScanRequestsAsync(organizationId, cancellationToken);
        bool mayBringInSales = await access.HasPermission(organizationId, HaulScoutPermissions.ManageFinance, cancellationToken);
        List<SyncScanRequest> queued = [];
        List<SkippedScanDto> skipped = [];
        foreach (ChannelAccount account in targets)
        {
            SyncPlatform? platform = SyncPlatforms.Find(account.Channel);
            IEnumerable<SyncLoopKind> loops = request.Scope switch
            {
                SyncScanScope.Listings => [SyncLoopKind.Listings],
                SyncScanScope.Everything => platform?.Loops ?? [SyncLoopKind.Sales],
                _ => [SyncLoopKind.Sales],
            };
            DateTimeOffset? from = request.Scope == SyncScanScope.History ? request.From?.ToUniversalTime() : null;

            if (from is not null && platform is not null && from < now.AddDays(-platform.HistoryDays))
            {
                skipped.Add(new SkippedScanDto(account.Id, account.DisplayName,
                    $"{platform.Platform}'s sales ledger reaches back {platform.HistoryDays} days. For older sales, import the order report."));
                continue;
            }

            foreach (SyncLoopKind loop in loops)
            {
                if (SyncRules.Refusal(account, loop, options, extensionVersion: null) is AppResult refusal)
                {
                    skipped.Add(new SkippedScanDto(account.Id, account.DisplayName, refusal.Message!));
                    continue;
                }

                if (loop == SyncLoopKind.Sales && !mayBringInSales)
                {
                    skipped.Add(new SkippedScanDto(account.Id, account.DisplayName, SalesNeedFinance));
                    continue;
                }

                SyncScanRequest? same = open.FirstOrDefault(candidate =>
                    candidate.ChannelAccountId == account.Id && candidate.Loop == loop && candidate.IsOpen && candidate.From == from);
                if (same is not null)
                {
                    queued.Add(same);
                    continue;
                }

                SyncScanRequest created = new()
                {
                    OrganizationId = organizationId,
                    ChannelAccountId = account.Id,
                    Loop = loop,
                    From = from,
                    RequestedByUserId = currentUser.UserId,
                    RequestedAt = now,
                };
                store.Add(created);
                queued.Add(created);
            }
        }

        if (queued.Count == 0 && request.ChannelAccountId is not null && skipped.Count > 0)
        {
            return AppResult<RequestScanResultDto>.Validation(skipped[0].Reason);
        }

        await store.SaveChangesAsync(cancellationToken);
        IReadOnlyList<SyncAgent> agents = await store.ListAgentsAsync(organizationId, cancellationToken);
        return AppResult<RequestScanResultDto>.Ok(new RequestScanResultDto(
            queued.Distinct().Select(item => Request(item, accounts, agents)).ToList(),
            skipped));
    }

    /// <summary>Withdraws a scan that has not started. One already running finishes — stopping it half-way helps nobody.</summary>
    public async Task<AppResult> CancelScanAsync(Guid requestId, CancellationToken cancellationToken)
    {
        SyncScanRequest? request = await store.FindScanRequestAsync(currentUser.OrganizationId, requestId, cancellationToken);
        if (request is null)
        {
            return AppResult.NotFound("That scan request was not found.");
        }

        if (request.Status == SyncScanStatus.Running)
        {
            return AppResult.Conflict("It is already running and finishes shortly.");
        }

        if (request.Status != SyncScanStatus.Queued)
        {
            return AppResult.Conflict("It has already finished.");
        }

        SyncSweep.Finish(request, SyncScanStatus.Cancelled, "Cancelled.", clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult<SyncAgentDto>> RenameAgentAsync(Guid agentId, RenameSyncAgentRequest request, CancellationToken cancellationToken)
    {
        SyncAgent? agent = await store.FindAgentAsync(currentUser.OrganizationId, agentId, cancellationToken);
        if (agent is null)
        {
            return AppResult<SyncAgentDto>.NotFound("That browser was not found.");
        }

        string? name = Text.TrimToNull(request.Name);
        if (name is null || name.Length > 80)
        {
            return AppResult<SyncAgentDto>.Validation("Give the browser a name of up to 80 characters.");
        }

        agent.Name = name;
        await store.SaveChangesAsync(cancellationToken);
        IReadOnlyDictionary<Guid, string> people = await store.UserNamesAsync([agent.PairedByUserId], cancellationToken);
        return AppResult<SyncAgentDto>.Ok(Agent(agent, people, clock.GetUtcNow()));
    }

    /// <summary>
    /// Disconnects a browser: its token stops working at once, and anything it was running is closed
    /// out so another browser can take over.
    /// </summary>
    public async Task<AppResult> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        SyncAgent? agent = await store.FindAgentAsync(organizationId, agentId, cancellationToken);
        if (agent is null)
        {
            return AppResult.NotFound("That browser was not found.");
        }

        await SyncSweep.DisconnectAsync(store, agent, currentUser.UserId, clock.GetUtcNow(), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    /// <summary>A one-time code for pairing a browser, shown once and valid for minutes.</summary>
    public async Task<CreateSyncPairingResultDto> CreatePairingAsync(CancellationToken cancellationToken)
    {
        string code = SyncSecrets.NewPairingCode();
        SyncPairing pairing = new()
        {
            OrganizationId = currentUser.OrganizationId,
            CodeHash = SyncSecrets.HashPairingCode(SyncSecrets.NormalizePairingCode(code)!),
            CreatedByUserId = currentUser.UserId,
            ExpiresAt = clock.GetUtcNow().AddMinutes(options.PairingCodeMinutes),
        };
        store.Add(pairing);
        await store.SaveChangesAsync(cancellationToken);
        return new CreateSyncPairingResultDto(pairing.Id, code, pairing.ExpiresAt);
    }

    public async Task<AppResult<SyncPairingDto>> GetPairingAsync(Guid pairingId, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        SyncPairing? pairing = await store.FindPairingAsync(organizationId, pairingId, cancellationToken);
        if (pairing is null)
        {
            return AppResult<SyncPairingDto>.NotFound("That pairing was not found.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (pairing.SyncAgentId is Guid agentId && await store.FindAgentAsync(organizationId, agentId, cancellationToken) is SyncAgent agent)
        {
            IReadOnlyDictionary<Guid, string> people = await store.UserNamesAsync([agent.PairedByUserId], cancellationToken);
            return AppResult<SyncPairingDto>.Ok(new SyncPairingDto(pairing.Id, SyncPairingStatus.Paired, pairing.ExpiresAt, Agent(agent, people, now)));
        }

        SyncPairingStatus status = pairing.ExpiresAt <= now ? SyncPairingStatus.Expired : SyncPairingStatus.Waiting;
        return AppResult<SyncPairingDto>.Ok(new SyncPairingDto(pairing.Id, status, pairing.ExpiresAt, null));
    }

    private SyncAccountDto Account(ChannelAccount account, IReadOnlyList<SyncLoop> loops, IReadOnlyList<SyncAgent> agents, DateTimeOffset now)
    {
        SyncPlatform? platform = SyncPlatforms.Find(account.Channel);
        List<SyncAgent> live = agents.Where(agent => agent.RevokedAt is null).ToList();
        List<(SyncAgent Agent, SyncAgentMarketplace Marketplace)> sessions = platform is null
            ? []
            : live.SelectMany(agent => agent.Marketplaces
                    .Where(marketplace => string.Equals(marketplace.Platform, platform.Platform, StringComparison.OrdinalIgnoreCase))
                    .Select(marketplace => (agent, marketplace)))
                .ToList();

        List<SyncLoopDto> loopDtos = platform is null
            ? []
            : platform.Loops.Select(kind =>
            {
                SyncLoop? loop = loops.FirstOrDefault(candidate => candidate.ChannelAccountId == account.Id && candidate.Kind == kind);
                string? runningOn = loop?.LeaseAgentId is Guid holder && loop.LeaseExpiresAt > now
                    ? agents.FirstOrDefault(agent => agent.Id == holder)?.Name
                    : null;
                DateTimeOffset? due = SyncWorkPlanner.DueAt(account.EveryMinutes(kind), loop);
                return new SyncLoopDto(
                    kind,
                    account.EveryMinutes(kind),
                    loop?.LastStartedAt,
                    loop?.LastSucceededAt,
                    loop?.LastRunId,
                    loop?.LastError,
                    loop?.ConsecutiveFailures ?? 0,
                    runningOn,
                    due is null ? null : due < now ? now : due);
            }).ToList();

        (SyncAccountHealth health, string message) = Health(account, platform, sessions, now);
        return new SyncAccountDto(
            account.Id,
            platform?.Platform ?? account.Channel,
            account.DisplayName,
            platform is not null,
            platform?.Loops ?? [],
            platform is not null && platform.HistoryDays > 0,
            account.BoundUsername,
            account.BoundAt,
            health,
            message,
            loopDtos,
            sessions.Select(session => new SyncSessionSeenDto(
                session.Agent.Id,
                session.Agent.Name,
                IsOnline(session.Agent, now),
                session.Marketplace.AccessGranted,
                session.Marketplace.SignedIn,
                session.Marketplace.Username,
                session.Marketplace.MarketplaceUserId,
                session.Marketplace.PausedUntil > now ? session.Marketplace.PausedUntil : null,
                session.Marketplace.PausedUntil > now ? session.Marketplace.PauseReason : null)).ToList());
    }

    /// <summary>One sentence on whether the account can sync right now, and if not, what would fix it.</summary>
    private (SyncAccountHealth Health, string Message) Health(
        ChannelAccount account,
        SyncPlatform? platform,
        IReadOnlyList<(SyncAgent Agent, SyncAgentMarketplace Marketplace)> sessions,
        DateTimeOffset now)
    {
        if (platform is null)
        {
            return (SyncAccountHealth.Unsupported, $"{account.Channel} cannot be synced from a browser yet. Import its exports meanwhile.");
        }

        SyncPlatformSwitch toggle = options.For(platform.Platform);
        if (!toggle.Enabled)
        {
            return (SyncAccountHealth.Disabled, toggle.DisabledReason ?? $"Sync for {platform.Platform} is switched off for now.");
        }

        List<(SyncAgent Agent, SyncAgentMarketplace Marketplace)> online = sessions.Where(session => IsOnline(session.Agent, now)).ToList();
        if (!account.IsBound)
        {
            string[] signedIn = online
                .Where(session => session.Marketplace.SignedIn == true && session.Marketplace.Username is not null)
                .Select(session => session.Marketplace.Username!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return (SyncAccountHealth.NotBound, signedIn.Length > 0
                ? $"Bind it to the {platform.Platform} account it should sync — a browser is signed in as {string.Join(" and ", signedIn)}."
                : $"Sign in to {platform.Platform} in a paired browser, then bind it to that account.");
        }

        List<(SyncAgent Agent, SyncAgentMarketplace Marketplace)> able = online
            .Where(session => session.Marketplace.AccessGranted
                && session.Marketplace.SignedIn == true
                && SyncWorkPlanner.SameMarketplaceUser(session.Marketplace.Username, account.BoundUsername))
            .ToList();
        if (able.Count == 0)
        {
            string[] others = online
                .Where(session => session.Marketplace.SignedIn == true && session.Marketplace.Username is not null)
                .Select(session => session.Marketplace.Username!)
                .Where(username => !SyncWorkPlanner.SameMarketplaceUser(username, account.BoundUsername))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            bool noAccess = online.Any(session => !session.Marketplace.AccessGranted);
            return (SyncAccountHealth.NoBrowser,
                others.Length > 0 ? $"The browsers online are signed in to {platform.Platform} as {string.Join(" and ", others)}, not {account.BoundUsername}."
                : noAccess ? $"Allow the HaulScout extension to reach {platform.Platform} in its popup."
                : $"No browser signed in to {platform.Platform} as {account.BoundUsername} is online.");
        }

        List<(SyncAgent Agent, SyncAgentMarketplace Marketplace)> running = able.Where(session => !(session.Marketplace.PausedUntil > now)).ToList();
        if (running.Count == 0)
        {
            // The time itself is in each session's PausedUntil, for the portal to show in local time.
            (SyncAgent _, SyncAgentMarketplace soonest) = able.MinBy(session => session.Marketplace.PausedUntil);
            return (SyncAccountHealth.Paused, $"Paused on every browser that can sync it: {soonest.PauseReason ?? $"{platform.Platform} asked for a break."}");
        }

        return (SyncAccountHealth.Ready, $"Ready on {string.Join(", ", running.Select(session => session.Agent.Name).Distinct())}.");
    }

    private SyncAgentDto Agent(SyncAgent agent, IReadOnlyDictionary<Guid, string> people, DateTimeOffset now) => new(
        agent.Id,
        agent.Name,
        agent.Browser,
        agent.OperatingSystem,
        agent.ExtensionVersion,
        agent.PairedAt,
        people.GetValueOrDefault(agent.PairedByUserId) ?? "a former member",
        agent.LastSeenAt,
        IsOnline(agent, now),
        agent.RevokedAt is not null,
        agent.Marketplaces
            .OrderBy(marketplace => marketplace.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(marketplace => new SyncAgentMarketplaceDto(
                marketplace.Platform,
                marketplace.AccessGranted,
                marketplace.SignedIn,
                marketplace.Username,
                marketplace.PausedUntil > now ? marketplace.PausedUntil : null,
                marketplace.PausedUntil > now ? marketplace.PauseReason : null,
                marketplace.ObservedAt))
            .ToList());

    private static SyncScanRequestDto Request(SyncScanRequest request, IReadOnlyList<ChannelAccount> accounts, IReadOnlyList<SyncAgent> agents)
    {
        ChannelAccount? account = accounts.FirstOrDefault(candidate => candidate.Id == request.ChannelAccountId);
        return new SyncScanRequestDto(
            request.Id,
            request.ChannelAccountId,
            account?.DisplayName ?? "Removed account",
            account?.Channel ?? string.Empty,
            request.Loop,
            request.From,
            request.Status,
            request.RequestedAt,
            request.StartedAt,
            request.FinishedAt,
            request.SyncAgentId is Guid agentId ? agents.FirstOrDefault(agent => agent.Id == agentId)?.Name : null,
            request.SyncRunId,
            request.Message);
    }

    private SyncPlatformSettingsDto Settings(SyncPlatform platform)
    {
        SyncPlatformSwitch toggle = options.For(platform.Platform);
        return new SyncPlatformSettingsDto(
            platform.Platform,
            platform.Loops,
            platform.SuggestedSalesMinutes,
            platform.MinSalesMinutes,
            platform.SuggestedListingsMinutes,
            platform.MinListingsMinutes,
            platform.HistoryDays,
            toggle.Enabled,
            toggle.Enabled ? null : toggle.DisabledReason);
    }

    private bool IsOnline(SyncAgent agent, DateTimeOffset now) =>
        agent.RevokedAt is null && agent.LastSeenAt >= now.AddSeconds(-options.OnlineWithinSeconds);
}
