using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using Microsoft.Extensions.Options;

namespace HaulScout.Application.Sync.Agents;

/// <summary>
/// The sync agent's side of the conversation: pairing, the heartbeat, and each run from lease to finish.
///
/// The brain stays here. The extension reads marketplaces and reports what it saw; what is due, who
/// may run it, whether the browser is signed in as the right account, and what the data means are
/// all decided on this side, where they are tested. What an agent delivers lands through the same
/// ingest as a file drop, so a scan and an export of the same order can never disagree about it.
/// </summary>
public sealed class SyncAgentService(
    ISyncAgentStore store,
    ISyncRunStore runs,
    ISyncAgentContext agent,
    ICurrentUserContext currentUser,
    IOrganizationStore organizations,
    IOrganizationAuthorizer access,
    PlatformOrderIngestionService orderIngestion,
    IPlatformListingIngestion listingIngestion,
    TimeProvider clock,
    IOptions<SyncAgentOptions> optionsAccessor)
{
    public const int MaxOrdersPerBatch = 200;
    public const int MaxListingsPerBatch = 500;
    public const int MaxIssuesPerBatch = 200;

    private readonly SyncAgentOptions options = optionsAccessor.Value;

    /// <summary>Exchanges a one-time code for a device token. The only anonymous step.</summary>
    public async Task<AppResult<PairAgentResultDto>> PairAsync(PairAgentRequest request, CancellationToken cancellationToken)
    {
        const string invalid = "That code has expired or was already used. Ask for a new one in HaulScout under Sync → Browsers.";
        string? code = SyncSecrets.NormalizePairingCode(request.Code);
        if (code is null)
        {
            return AppResult<PairAgentResultDto>.Validation("That is not a pairing code. Codes look like 7KQ2-MX9D.", AppErrorCode.SyncPairingInvalid);
        }

        DateTimeOffset now = clock.GetUtcNow();
        SyncPairing? pairing = await store.FindPairingByCodeAsync(SyncSecrets.HashPairingCode(code), cancellationToken);
        if (pairing is null || pairing.ConsumedAt is not null || pairing.ExpiresAt <= now
            || !await store.IsMemberAsync(pairing.OrganizationId, pairing.CreatedByUserId, cancellationToken))
        {
            return AppResult<PairAgentResultDto>.Validation(invalid, AppErrorCode.SyncPairingInvalid);
        }

        string browser = Required(request.Browser, 40) ?? "Browser";
        string? operatingSystem = SyncText.Clip(Text.TrimToNull(request.OperatingSystem), 40);
        string token = SyncSecrets.NewToken();
        SyncAgent created = new()
        {
            OrganizationId = pairing.OrganizationId,
            Name = Required(request.Name, 80) ?? (operatingSystem is null ? browser : $"{browser} on {operatingSystem}"),
            Browser = browser,
            OperatingSystem = operatingSystem,
            ExtensionVersion = Required(request.ExtensionVersion, 24) ?? "unknown",
            PairedByUserId = pairing.CreatedByUserId,
            PairedAt = now,
            TokenHash = SyncSecrets.HashToken(token),
            TokenIssuedAt = now,
            LastSeenAt = now,
        };
        store.Add(created);
        pairing.ConsumedAt = now;
        pairing.SyncAgentId = created.Id;
        await store.SaveChangesAsync(cancellationToken);

        string organizationName = (await organizations.FindAsync(pairing.OrganizationId, cancellationToken))?.Name ?? "your organization";
        return AppResult<PairAgentResultDto>.Ok(new PairAgentResultDto(created.Id, created.Name, token, organizationName));
    }

    /// <summary>
    /// The browser checking in: what it can see, in return for what it should do. Also where the
    /// device token rotates, and where abandoned work is closed out.
    /// </summary>
    public async Task<AppResult<AgentHeartbeatDto>> HeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        SyncAgent? self = await store.FindAgentAsync(organizationId, agent.AgentId, cancellationToken);
        if (self is null)
        {
            return AppResult<AgentHeartbeatDto>.NotFound("This browser is no longer paired.");
        }

        self.LastSeenAt = now;
        self.ExtensionVersion = Required(request.ExtensionVersion, 24) ?? self.ExtensionVersion;
        self.Browser = Required(request.Browser, 40) ?? self.Browser;
        self.OperatingSystem = SyncText.Clip(Text.TrimToNull(request.OperatingSystem), 40) ?? self.OperatingSystem;
        RecordMarketplaces(self, request.Marketplaces ?? [], now);
        string? newToken = Rotate(self, now);

        await SyncSweep.RunAsync(store, organizationId, now, options, cancellationToken);
        IReadOnlyList<ChannelAccount> accounts = await store.ListAccountsAsync(organizationId, cancellationToken);
        IReadOnlyList<SyncLoop> loops = await store.ListLoopsAsync(organizationId, cancellationToken);
        IReadOnlyList<SyncScanRequest> open = await store.ListOpenScanRequestsAsync(organizationId, cancellationToken);
        IReadOnlyList<AgentWorkDto> work = SyncWorkPlanner.Plan(
            now, self.Id, self.ExtensionVersion, accounts, loops, open, [.. self.Marketplaces], options);
        await store.SaveChangesAsync(cancellationToken);

        string organizationName = (await organizations.FindAsync(organizationId, cancellationToken))?.Name ?? string.Empty;
        return AppResult<AgentHeartbeatDto>.Ok(new AgentHeartbeatDto(
            self.Id,
            self.Name,
            organizationName,
            options.HeartbeatSeconds,
            SyncPlatforms.All.Select(platform => Policy(platform, self.ExtensionVersion)).ToList(),
            accounts
                .Where(account => account.IsActive && !account.IsNativelySynced && SyncPlatforms.Find(account.Channel) is not null)
                .OrderBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(account => new AgentAccountDto(
                    account.Id, SyncPlatforms.Find(account.Channel)!.Platform, account.DisplayName, account.BoundUsername,
                    account.SalesScanEveryMinutes, account.ListingsScanEveryMinutes))
                .ToList(),
            work,
            now,
            newToken));
    }

    /// <summary>The browser leaving on its own — "Disconnect this browser" in the extension.</summary>
    public async Task<AppResult> DisconnectAsync(CancellationToken cancellationToken)
    {
        SyncAgent? self = await store.FindAgentAsync(currentUser.OrganizationId, agent.AgentId, cancellationToken);
        if (self is null)
        {
            return AppResult.NotFound("This browser is no longer paired.");
        }

        await SyncSweep.DisconnectAsync(store, self, currentUser.UserId, clock.GetUtcNow(), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    /// <summary>
    /// Takes the lease on one loop of one account and opens its run — or says why not. Nothing is read
    /// from the marketplace until this succeeds.
    /// </summary>
    public async Task<AppResult<AgentRunDto>> StartRunAsync(StartAgentRunRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        SyncAgent? self = await store.FindAgentAsync(organizationId, agent.AgentId, cancellationToken);
        ChannelAccount? account = await store.FindAccountAsync(organizationId, request.ChannelAccountId, cancellationToken);
        if (self is null || account is null)
        {
            return AppResult<AgentRunDto>.NotFound("That account was not found.");
        }

        if (SyncRules.Refusal(account, request.Loop, options, self.ExtensionVersion) is AppResult refused)
        {
            return AppResult<AgentRunDto>.Validation(refused.Message!, refused.Code);
        }

        if (!SyncWorkPlanner.SameMarketplaceUser(request.SignedInAs, account.BoundUsername))
        {
            return AppResult<AgentRunDto>.Validation(
                $"This browser is signed in to {account.Channel} as {Text.TrimToNull(request.SignedInAs) ?? "nobody"}, but {account.DisplayName} syncs {account.BoundUsername}.",
                AppErrorCode.SyncWrongAccount);
        }

        if (await PermissionRefusal(organizationId, request.Loop, cancellationToken) is string notPermitted)
        {
            return AppResult<AgentRunDto>.Validation(notPermitted, AppErrorCode.SyncNotPermitted);
        }

        // A lease that lapsed mid-run is closed out first, so taking the loop over never leaves the
        // previous browser's run looking as if it were still going.
        await SyncSweep.RunAsync(store, organizationId, now, options, cancellationToken);

        SyncScanRequest? scanRequest = null;
        if (request.ScanRequestId is Guid requestId)
        {
            scanRequest = await store.FindScanRequestAsync(organizationId, requestId, cancellationToken);
            if (scanRequest is null || scanRequest.ChannelAccountId != account.Id || scanRequest.Loop != request.Loop)
            {
                return AppResult<AgentRunDto>.NotFound("That scan request was not found.");
            }

            if (scanRequest.Status != SyncScanStatus.Queued)
            {
                return AppResult<AgentRunDto>.Conflict("That scan request is no longer waiting.", AppErrorCode.SyncRunFinished);
            }
        }

        SyncLoop? loop = await store.FindLoopAsync(organizationId, account.Id, request.Loop, cancellationToken);
        if (loop is null)
        {
            loop = new SyncLoop { OrganizationId = organizationId, ChannelAccountId = account.Id, Kind = request.Loop };
            store.Add(loop);
        }
        else if (loop.LeaseAgentId is not null && loop.LeaseExpiresAt > now)
        {
            return AppResult<AgentRunDto>.Conflict(
                loop.LeaseAgentId == self.Id ? "This browser is already running it." : "Another browser is already running it.",
                AppErrorCode.SyncLeaseHeld);
        }

        SyncRun run = new()
        {
            OrganizationId = organizationId,
            ChannelAccountId = account.Id,
            Source = SyncSource.Agent,
            FormatKey = FormatKey(account.Channel, request.Loop),
            Platform = account.Channel,
            Status = SyncRunStatus.Running,
            StartedAt = now,
            TriggeredByUserId = scanRequest?.RequestedByUserId ?? self.PairedByUserId,
            SyncAgentId = self.Id,
            Loop = request.Loop,
            Trigger = scanRequest is null && request.Trigger == SyncTrigger.Requested ? SyncTrigger.Scheduled : request.Trigger,
            ScanRequestId = scanRequest?.Id,
        };
        runs.Add(run);

        loop.LeaseAgentId = self.Id;
        loop.LeaseExpiresAt = now.AddMinutes(options.LeaseMinutes);
        loop.LeaseRunId = run.Id;
        loop.LastStartedAt = now;
        loop.Version = Guid.NewGuid();

        if (scanRequest is not null)
        {
            scanRequest.Status = SyncScanStatus.Running;
            scanRequest.SyncAgentId = self.Id;
            scanRequest.StartedAt = now;
            scanRequest.SyncRunId = run.Id;
        }

        // Two browsers can reach this line for the same loop at once; the loop's version (or its
        // unique key, for a first run) lets exactly one of them save.
        if (!await store.TrySaveChangesAsync(cancellationToken))
        {
            return AppResult<AgentRunDto>.Conflict("Another browser started it a moment ago.", AppErrorCode.SyncLeaseHeld);
        }

        return AppResult<AgentRunDto>.Ok(new AgentRunDto(run.Id, loop.LeaseExpiresAt.Value, loop.Cursor, scanRequest?.From));
    }

    /// <summary>Lands one slice of what a run read, through the same ingest a file uses, and renews the lease.</summary>
    public async Task<AppResult<AgentRunBatchResultDto>> AddBatchAsync(Guid runId, AgentRunBatchRequest batch, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        (SyncRun? run, SyncLoop? loop, AppResult? problem) = await HeldRunAsync(organizationId, runId, now, cancellationToken);
        if (problem is not null)
        {
            return AppResult<AgentRunBatchResultDto>.Refused(problem);
        }

        IReadOnlyList<IngestPlatformOrderInput> orders = batch.Orders ?? [];
        IReadOnlyList<IngestPlatformListingInput> listings = batch.Listings ?? [];
        IReadOnlyList<AgentRunIssueInput> issues = batch.Issues ?? [];
        if (orders.Count > MaxOrdersPerBatch || listings.Count > MaxListingsPerBatch || issues.Count > MaxIssuesPerBatch)
        {
            return AppResult<AgentRunBatchResultDto>.Validation(
                $"A batch carries at most {MaxOrdersPerBatch} orders, {MaxListingsPerBatch} listings and {MaxIssuesPerBatch} issues.");
        }

        if ((run!.Loop == SyncLoopKind.Sales && listings.Count > 0) || (run.Loop == SyncLoopKind.Listings && orders.Count > 0))
        {
            return AppResult<AgentRunBatchResultDto>.Validation("A sales run delivers orders and a listings run delivers listings.");
        }

        if (await PermissionRefusal(organizationId, run.Loop!.Value, cancellationToken) is string notPermitted)
        {
            return AppResult<AgentRunBatchResultDto>.Validation(notPermitted, AppErrorCode.SyncNotPermitted);
        }

        // Renew the lease before landing anything: if another browser has taken the loop over, this
        // one learns it now and stops, rather than after writing a batch the other is also writing.
        loop!.LeaseExpiresAt = now.AddMinutes(options.LeaseMinutes);
        loop.Version = Guid.NewGuid();
        if (!await store.TrySaveChangesAsync(cancellationToken))
        {
            return AppResult<AgentRunBatchResultDto>.Conflict("Another browser took this over.", AppErrorCode.SyncLeaseLost);
        }

        int created = 0, updated = 0, unchanged = 0;
        if (orders.Count > 0)
        {
            (IngestPlatformOrdersResultDto? result, string? error) =
                await orderIngestion.IngestAsync(run.Platform, orders, run.ChannelAccountId, cancellationToken);
            if (error is not null)
            {
                return AppResult<AgentRunBatchResultDto>.Validation(error);
            }

            (created, updated, unchanged) = (result!.Created, result.Updated, result.Unchanged);
        }

        if (listings.Count > 0)
        {
            AppResult<IngestPlatformListingsResultDto> result = await listingIngestion.IngestAsync(
                new IngestPlatformListingsRequest(run.Platform, listings, run.ChannelAccountId), cancellationToken);
            if (result.Status != AppResultStatus.Ok || result.Value is null)
            {
                return AppResult<AgentRunBatchResultDto>.Validation(result.Message ?? "The listings could not be recorded.");
            }

            (created, updated, unchanged) = (result.Value.Created, result.Value.Updated, result.Value.Unchanged);
        }

        run.Created += created;
        run.Updated += updated;
        run.Unchanged += unchanged;
        run.Rejected += issues.Count(issue => issue.Rejected);
        runs.AddIssues(issues.Select(issue => new SyncRunIssue
        {
            OrganizationId = organizationId,
            SyncRunId = run.Id,
            Severity = issue.Rejected ? SyncIssueSeverity.Error : SyncIssueSeverity.Warning,
            ExternalReference = SyncText.Clip(Text.TrimToNull(issue.ExternalReference), 200),
            Message = SyncText.Clip(Text.TrimToNull(issue.Message), 1000) ?? "No detail given.",
        }));
        await store.SaveChangesAsync(cancellationToken);

        return AppResult<AgentRunBatchResultDto>.Ok(new AgentRunBatchResultDto(created, updated, unchanged, loop.LeaseExpiresAt.Value));
    }

    /// <summary>Closes a run that read everything it set out to, and records where the loop got to.</summary>
    public async Task<AppResult<SyncRunDto>> CompleteRunAsync(Guid runId, CompleteAgentRunRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        (SyncRun? run, SyncLoop? loop, AppResult? problem) = await HeldRunAsync(organizationId, runId, now, cancellationToken, requireLiveLease: false);
        if (problem is not null)
        {
            return AppResult<SyncRunDto>.Refused(problem);
        }

        int issueCount = (await runs.ListIssuesAsync(organizationId, runId, cancellationToken)).Count;
        run!.CoversFrom = request.CoversFrom?.ToUniversalTime();
        run.CoversTo = request.CoversTo?.ToUniversalTime();
        run.IsCompleteSnapshot = run.Loop == SyncLoopKind.Listings && request.CompleteSnapshot;
        run.Status = issueCount > 0 ? SyncRunStatus.CompletedWithIssues : SyncRunStatus.Succeeded;
        run.Summary = Summary(run);
        run.FinishedAt = now;

        loop!.Cursor = SyncText.Clip(Text.TrimToNull(request.Cursor), 400) ?? loop.Cursor;
        loop.LastFinishedAt = now;
        loop.LastSucceededAt = now;
        loop.LastRunId = run.Id;
        loop.LastError = null;
        loop.ConsecutiveFailures = 0;
        SyncSweep.ReleaseLease(loop);

        await FinishRequestAsync(organizationId, run, SyncScanStatus.Succeeded, run.Summary, now, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SyncRunDto>.Ok(SyncRunDtos.From(run, fileName: null, issueCount));
    }

    /// <summary>Closes a run that could not finish, with the reason in the operator's words.</summary>
    public async Task<AppResult<SyncRunDto>> FailRunAsync(Guid runId, FailAgentRunRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        (SyncRun? run, SyncLoop? loop, AppResult? problem) = await HeldRunAsync(organizationId, runId, now, cancellationToken, requireLiveLease: false);
        if (problem is not null)
        {
            return AppResult<SyncRunDto>.Refused(problem);
        }

        string summary = SyncText.Clip(FailureSummary(run!.Platform, request.Kind, request.Message), 500)!;
        run.Status = SyncRunStatus.Failed;
        run.Summary = summary;
        run.FinishedAt = now;

        loop!.LastFinishedAt = now;
        loop.LastRunId = run.Id;
        loop.LastError = summary;
        loop.ConsecutiveFailures++;
        SyncSweep.ReleaseLease(loop);

        await FinishRequestAsync(organizationId, run, SyncScanStatus.Failed, summary, now, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        int issueCount = (await runs.ListIssuesAsync(organizationId, runId, cancellationToken)).Count;
        return AppResult<SyncRunDto>.Ok(SyncRunDtos.From(run, fileName: null, issueCount));
    }

    /// <summary>
    /// The run, its loop, and whether this browser still holds it. A finished run, someone else's run,
    /// or a lease another browser has since taken are all refusals the agent acts on by stopping.
    /// </summary>
    private async Task<(SyncRun? Run, SyncLoop? Loop, AppResult? Problem)> HeldRunAsync(
        Guid organizationId, Guid runId, DateTimeOffset now, CancellationToken cancellationToken, bool requireLiveLease = true)
    {
        SyncRun? run = await runs.FindRunAsync(organizationId, runId, cancellationToken);
        if (run is null || run.SyncAgentId != agent.AgentId || run.Loop is null || run.ChannelAccountId is null)
        {
            return (null, null, AppResult.NotFound("That run was not found."));
        }

        if (run.Status != SyncRunStatus.Running)
        {
            return (null, null, AppResult.Conflict("That run has already finished.", AppErrorCode.SyncRunFinished));
        }

        SyncLoop? loop = await store.FindLoopAsync(organizationId, run.ChannelAccountId.Value, run.Loop.Value, cancellationToken);
        bool stillMine = loop is not null && loop.LeaseRunId == run.Id && loop.LeaseAgentId == agent.AgentId
            && (!requireLiveLease || loop.LeaseExpiresAt > now);
        return stillMine
            ? (run, loop, null)
            : (null, null, AppResult.Conflict("This browser's lease on that run has lapsed; it runs again on its next turn.", AppErrorCode.SyncLeaseLost));
    }

    private async Task FinishRequestAsync(Guid organizationId, SyncRun run, SyncScanStatus status, string? message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (run.ScanRequestId is Guid requestId
            && await store.FindScanRequestAsync(organizationId, requestId, cancellationToken) is SyncScanRequest request
            && request.Status == SyncScanStatus.Running)
        {
            SyncSweep.Finish(request, status, message, now);
        }
    }

    private async Task<string?> PermissionRefusal(Guid organizationId, SyncLoopKind loop, CancellationToken cancellationToken)
    {
        // The browser writes as the person who paired it, with that person's permissions today — the
        // same rule a file import follows for the same data.
        (string permission, string what) = loop == SyncLoopKind.Sales
            ? (HaulScoutPermissions.ManageFinance, "sales")
            : (HaulScoutPermissions.ManageListings, "listings");
        return await access.HasPermission(organizationId, permission, cancellationToken)
            ? null
            : $"The person who paired this browser can no longer record {what}. Pair it again as someone who can.";
    }

    private AgentPlatformPolicyDto Policy(SyncPlatform platform, string extensionVersion)
    {
        SyncPlatformSwitch toggle = options.For(platform.Platform);
        bool enabled = toggle.AllowsVersion(extensionVersion);
        return new AgentPlatformPolicyDto(
            platform.Platform,
            enabled,
            enabled ? null : toggle.DisabledReason ?? $"Sync for {platform.Platform} is switched off for now.",
            platform.Loops,
            platform.MinSecondsBetweenRequests,
            platform.MaxRequestsPerMinute,
            platform.MaxRequestsPerDay,
            platform.HistoryDays);
    }

    private void RecordMarketplaces(SyncAgent self, IReadOnlyList<AgentMarketplaceReport> reports, DateTimeOffset now)
    {
        Dictionary<string, AgentMarketplaceReport> byPlatform = reports
            .Where(report => SyncPlatforms.Find(report.Platform) is not null)
            .GroupBy(report => SyncPlatforms.Find(report.Platform)!.Platform, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach ((string platform, AgentMarketplaceReport report) in byPlatform)
        {
            SyncAgentMarketplace? marketplace = self.Marketplaces.FirstOrDefault(item =>
                string.Equals(item.Platform, platform, StringComparison.OrdinalIgnoreCase));
            if (marketplace is null)
            {
                // Added through the store, not only the collection: a new row with a key already set
                // would otherwise be taken for an existing one and updated — which fails.
                marketplace = new SyncAgentMarketplace { OrganizationId = self.OrganizationId, SyncAgentId = self.Id, Platform = platform };
                store.Add(marketplace);
            }

            marketplace.AccessGranted = report.AccessGranted;
            marketplace.SignedIn = report.AccessGranted ? report.SignedIn : null;
            marketplace.Username = report.AccessGranted && report.SignedIn == true ? SyncText.Clip(Text.TrimToNull(report.Username), 120) : null;
            marketplace.MarketplaceUserId = marketplace.Username is null ? null : SyncText.Clip(Text.TrimToNull(report.MarketplaceUserId), 120);
            marketplace.PausedUntil = report.PausedUntil > now ? report.PausedUntil?.ToUniversalTime() : null;
            marketplace.PauseReason = marketplace.PausedUntil is null ? null : SyncText.Clip(Text.TrimToNull(report.PauseReason), 300);
            marketplace.ObservedAt = now;
        }
    }

    /// <summary>
    /// Rotates the device token on schedule, and keeps the previous one working until the browser is
    /// seen using the new one — a rotation whose answer was lost must not lock the browser out.
    /// </summary>
    private string? Rotate(SyncAgent self, DateTimeOffset now)
    {
        if (agent.UsedPreviousToken)
        {
            // It never received the last token. Issue another; the one it holds stays valid meanwhile.
            string reissued = SyncSecrets.NewToken();
            self.TokenHash = SyncSecrets.HashToken(reissued);
            self.TokenIssuedAt = now;
            return reissued;
        }

        if (self.PreviousTokenHash is not null)
        {
            self.PreviousTokenHash = null;
            return null;
        }

        if (now - self.TokenIssuedAt < TimeSpan.FromDays(options.TokenRotationDays))
        {
            return null;
        }

        string next = SyncSecrets.NewToken();
        self.PreviousTokenHash = self.TokenHash;
        self.TokenHash = SyncSecrets.HashToken(next);
        self.TokenIssuedAt = now;
        return next;
    }

    private static string Summary(SyncRun run)
    {
        string noun = run.Loop == SyncLoopKind.Sales ? "order" : "listing";
        if (run.Created + run.Updated + run.Unchanged == 0)
        {
            return run.Loop == SyncLoopKind.Sales ? "No sales in the period scanned." : "No live listings found.";
        }

        string counted = SyncText.Counted(run.Created, run.Updated, run.Unchanged, noun);
        return run.Rejected > 0 ? $"{counted} {run.Rejected} could not be read." : counted;
    }

    private static string FailureSummary(string platform, SyncFailureKind kind, string? detail)
    {
        string reason = kind switch
        {
            SyncFailureKind.SignedOut => $"The browser is not signed in to {platform}.",
            SyncFailureKind.WrongAccount => $"The browser is signed in to a different {platform} account.",
            SyncFailureKind.RateLimited => $"{platform} asked for a pause; the browser backed off.",
            SyncFailureKind.Challenge => $"{platform} showed a security check. Open {platform} in that browser and complete it.",
            SyncFailureKind.SiteChanged => $"{platform}'s pages no longer look the way the extension expects. It has paused {platform} until an update.",
            SyncFailureKind.Network => $"{platform} could not be reached.",
            _ => "The scan stopped.",
        };
        string? clean = Text.TrimToNull(detail);
        return clean is null ? reason : $"{reason} {clean}";
    }

    /// <summary>A stable key naming what an agent run was, beside the file formats in the history.</summary>
    public static string FormatKey(string platform, SyncLoopKind loop) =>
        $"agent.{platform.ToLowerInvariant()}.{loop.ToString().ToLowerInvariant()}";

    private static string? Required(string? value, int max) => SyncText.Clip(Text.TrimToNull(value), max);
}
