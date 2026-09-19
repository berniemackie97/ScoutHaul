using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Sync.Agents;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Organizations;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Sync;

namespace HaulScout.Tests;

/// <summary>
/// The sync agent's contract with the server: a browser pairs once, checks in, is offered only the
/// work it can do for the account it is signed in as, holds a lease while it runs, and lands what it
/// read through the same ingest as a file. Nothing runs on a schedule until a person turns one on.
/// </summary>
public sealed class SyncAgentTests
{
    private const string Seller = "overhaultradingcompany";

    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class StubAuthorizer(HashSet<string> granted) : IOrganizationAuthorizer
    {
        public Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            granted.Contains(permission) ? Task.CompletedTask : throw new UnauthorizedAccessException(permission);

        public Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(granted.Contains(permission));

        public Task RequireOwner(Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubAgent(Guid agentId, bool usedPreviousToken) : ISyncAgentContext
    {
        public Guid AgentId => agentId;
        public bool UsedPreviousToken => usedPreviousToken;
    }

    private sealed class Harness
    {
        public required HaulScoutDbContext Db { get; init; }
        public required Guid Org { get; init; }
        public required Guid User { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required SyncAgentOptions Options { get; init; }
        public required HashSet<string> Granted { get; init; }
        public required ChannelAccount Ebay { get; init; }
        public required ChannelAccount Depop { get; init; }
        public required SyncHubService Hub { get; init; }

        public SyncAgentService As(Guid agentId, bool usedPreviousToken = false)
        {
            StubCurrentUser currentUser = new(Org, User);
            OrderAssembler assembler = new(Db, currentUser);
            EfPlatformOrderStore orderStore = new(Db, assembler);
            PlatformOrderSuggestionService suggestions = new(new EfPlatformOrderSuggestionStore(Db, currentUser));
            PlatformOrderWorkflowService workflow = new(orderStore, currentUser, suggestions);
            PlatformOrderAutoMatchService autoMatch = new(suggestions, workflow, orderStore, currentUser);
            return new SyncAgentService(
                new EfSyncAgentStore(Db),
                new EfSyncRunStore(Db),
                new StubAgent(agentId, usedPreviousToken),
                currentUser,
                new EfOrganizationStore(Db),
                new StubAuthorizer(Granted),
                new PlatformOrderIngestionService(orderStore, currentUser, suggestions, workflow, autoMatch),
                new PlatformListingImportService(Db, currentUser),
                Clock,
                Microsoft.Extensions.Options.Options.Create(Options));
        }

        public async Task<(Guid AgentId, string Token)> PairAsync(string browser = "Firefox")
        {
            CreateSyncPairingResultDto pairing = await Hub.CreatePairingAsync(CancellationToken.None);
            AppResult<PairAgentResultDto> paired = await As(Guid.Empty).PairAsync(
                new PairAgentRequest(pairing.Code, browser, "1.0.0", "macOS"), CancellationToken.None);
            Assert.Equal(AppResultStatus.Ok, paired.Status);
            return (paired.Value!.AgentId, paired.Value.Token);
        }

        public async Task<AgentHeartbeatDto> HeartbeatAsync(Guid agentId, string? signedInAs = Seller, bool access = true)
        {
            AppResult<AgentHeartbeatDto> beat = await As(agentId).HeartbeatAsync(
                new AgentHeartbeatRequest("1.0.0", "Firefox",
                    [new AgentMarketplaceReport("eBay", access, SignedIn: signedInAs is not null, Username: signedInAs)]),
                CancellationToken.None);
            Assert.Equal(AppResultStatus.Ok, beat.Status);
            return beat.Value!;
        }

        /// <summary>A paired browser, signed in as the seller, with the eBay account bound to it.</summary>
        public async Task<Guid> ReadyAgentAsync(string browser = "Firefox")
        {
            (Guid agentId, _) = await PairAsync(browser);
            await HeartbeatAsync(agentId);
            if (!Ebay.IsBound)
            {
                Assert.Equal(AppResultStatus.Ok, (await Hub.BindAsync(Ebay.Id, new BindSyncAccountRequest(Seller), CancellationToken.None)).Status);
            }

            return agentId;
        }

        public Task<AppResult<AgentRunDto>> StartAsync(Guid agentId, SyncLoopKind loop, Guid? request = null, string signedInAs = Seller) =>
            As(agentId).StartRunAsync(
                new StartAgentRunRequest(Ebay.Id, loop, request is null ? SyncTrigger.Scheduled : SyncTrigger.Requested, signedInAs, request),
                CancellationToken.None);
    }

    private static Harness NewHarness(params string[] granted)
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org, UserId = user, Role = OrganizationRole.Owner });
        ChannelAccount ebay = new() { OrganizationId = org, Channel = "eBay", DisplayName = "Overhaul eBay" };
        ChannelAccount depop = new() { OrganizationId = org, Channel = "Depop", DisplayName = "Overhaul Depop" };
        db.ChannelAccounts.AddRange(ebay, depop);
        db.SaveChanges();

        FakeTimeProvider clock = new(new DateTimeOffset(2026, 9, 18, 20, 0, 0, TimeSpan.Zero));
        SyncAgentOptions syncOptions = new();
        HashSet<string> grantedSet = granted.Length == 0 ? [HaulScoutPermissions.ManageFinance, HaulScoutPermissions.ManageListings] : [.. granted];
        return new Harness
        {
            Db = db,
            Org = org,
            User = user,
            Clock = clock,
            Options = syncOptions,
            Granted = grantedSet,
            Ebay = ebay,
            Depop = depop,
            Hub = new SyncHubService(new EfSyncAgentStore(db), new StubCurrentUser(org, user), new StubAuthorizer(grantedSet), clock, Microsoft.Extensions.Options.Options.Create(syncOptions)),
        };
    }

    private static IngestPlatformOrderInput EbayOrder(string id, string itemId, decimal? fees = 4.08m) => new(
        ExternalOrderId: id,
        OrderedAt: new DateTimeOffset(2026, 9, 18, 7, 48, 0, TimeSpan.Zero),
        ItemsSubtotal: 20m,
        ShippingCharged: 6.99m,
        TaxCollected: 1.96m,
        FeeTotal: fees,
        Lines: [new IngestPlatformOrderLineInput("Sports Game Lot of 4", 20m, ExternalItemId: itemId)],
        Platform: "eBay",
        FeesPending: fees is null);

    // ── Secrets ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Pairing_codes_survive_being_read_aloud_and_retyped()
    {
        string code = SyncSecrets.NewPairingCode();
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", code);
        Assert.Equal(code.Replace("-", string.Empty), SyncSecrets.NormalizePairingCode($"  {code.ToLowerInvariant()} "));
        Assert.Equal("01234567", SyncSecrets.NormalizePairingCode("OI23-4567"));
        Assert.Equal("01234567", SyncSecrets.NormalizePairingCode("ol23 4567"));
        Assert.Null(SyncSecrets.NormalizePairingCode("1234-567"));
        Assert.Null(SyncSecrets.NormalizePairingCode("U234-5678"));
    }

    [Fact]
    public void Device_tokens_are_long_random_and_recognisable()
    {
        string first = SyncSecrets.NewToken();
        string second = SyncSecrets.NewToken();
        Assert.NotEqual(first, second);
        Assert.True(SyncSecrets.LooksLikeToken(first));
        Assert.False(SyncSecrets.LooksLikeToken("eyJhbGciOiJIUzI1NiJ9.a.b"));
        Assert.Equal(64, SyncSecrets.HashToken(first).Length);
        Assert.NotEqual(SyncSecrets.HashToken(first), SyncSecrets.HashPairingCode(first));
    }

    // ── Pairing ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_code_pairs_one_browser_once_and_only_its_hash_is_kept()
    {
        Harness harness = NewHarness();
        CreateSyncPairingResultDto pairing = await harness.Hub.CreatePairingAsync(CancellationToken.None);
        Assert.Equal(SyncPairingStatus.Waiting, (await harness.Hub.GetPairingAsync(pairing.PairingId, CancellationToken.None)).Value!.Status);

        AppResult<PairAgentResultDto> paired = await harness.As(Guid.Empty).PairAsync(
            new PairAgentRequest(pairing.Code.ToLowerInvariant(), "Firefox", "1.0.0", "macOS"), CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, paired.Status);
        Assert.Equal("Firefox on macOS", paired.Value!.AgentName);
        SyncAgent agent = await harness.Db.SyncAgents.SingleAsync();
        Assert.Equal(SyncSecrets.HashToken(paired.Value.Token), agent.TokenHash);
        Assert.DoesNotContain(paired.Value.Token, agent.TokenHash);
        Assert.Equal(harness.User, agent.PairedByUserId);

        SyncPairingDto status = (await harness.Hub.GetPairingAsync(pairing.PairingId, CancellationToken.None)).Value!;
        Assert.Equal(SyncPairingStatus.Paired, status.Status);
        Assert.Equal(agent.Id, status.Agent!.Id);

        AppResult<PairAgentResultDto> again = await harness.As(Guid.Empty).PairAsync(
            new PairAgentRequest(pairing.Code, "Chrome", "1.0.0"), CancellationToken.None);
        Assert.Equal(AppErrorCode.SyncPairingInvalid, again.Code);
        Assert.Single(await harness.Db.SyncAgents.ToListAsync());
    }

    [Fact]
    public async Task An_expired_code_pairs_nothing()
    {
        Harness harness = NewHarness();
        CreateSyncPairingResultDto pairing = await harness.Hub.CreatePairingAsync(CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromMinutes(harness.Options.PairingCodeMinutes + 1));

        AppResult<PairAgentResultDto> late = await harness.As(Guid.Empty).PairAsync(
            new PairAgentRequest(pairing.Code, "Firefox", "1.0.0"), CancellationToken.None);

        Assert.Equal(AppErrorCode.SyncPairingInvalid, late.Code);
        Assert.Equal(SyncPairingStatus.Expired, (await harness.Hub.GetPairingAsync(pairing.PairingId, CancellationToken.None)).Value!.Status);
    }

    // ── Heartbeat, binding and planning ────────────────────────────────────────────

    [Fact]
    public async Task Nothing_is_scheduled_until_a_person_switches_a_schedule_on()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();

        AgentHeartbeatDto idle = await harness.HeartbeatAsync(agent);
        Assert.Empty(idle.Work);
        Assert.Contains(idle.Accounts, account => account.ChannelAccountId == harness.Ebay.Id && account.SalesEveryMinutes is null);

        await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(10, null), CancellationToken.None);
        AgentHeartbeatDto due = await harness.HeartbeatAsync(agent);
        AgentWorkDto work = Assert.Single(due.Work);
        Assert.Equal((SyncLoopKind.Sales, SyncTrigger.Scheduled), (work.Loop, work.Trigger));
    }

    [Fact]
    public async Task Work_goes_only_to_a_browser_signed_in_as_the_bound_account()
    {
        Harness harness = NewHarness();
        Guid seller = await harness.ReadyAgentAsync();
        await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(10, 240), CancellationToken.None);
        (Guid personal, _) = await harness.PairAsync("Chrome");

        Assert.Empty((await harness.HeartbeatAsync(personal, signedInAs: "someones_personal_account")).Work);
        Assert.Empty((await harness.HeartbeatAsync(personal, signedInAs: null)).Work);
        Assert.Empty((await harness.HeartbeatAsync(seller, access: false)).Work);
        Assert.Equal(2, (await harness.HeartbeatAsync(seller)).Work.Count);

        AppResult<AgentRunDto> wrong = await harness.StartAsync(personal, SyncLoopKind.Sales, signedInAs: "someones_personal_account");
        Assert.Equal(AppErrorCode.SyncWrongAccount, wrong.Code);
        Assert.Empty(await harness.Db.SyncRuns.ToListAsync());
    }

    [Fact]
    public async Task Binding_accepts_only_an_account_a_browser_was_seen_signed_in_to()
    {
        Harness harness = NewHarness();
        (Guid agent, _) = await harness.PairAsync();

        AppResult<SyncOverviewDto> unseen = await harness.Hub.BindAsync(harness.Ebay.Id, new BindSyncAccountRequest(Seller), CancellationToken.None);
        Assert.Equal(AppResultStatus.Validation, unseen.Status);

        await harness.HeartbeatAsync(agent);
        AppResult<SyncOverviewDto> bound = await harness.Hub.BindAsync(harness.Ebay.Id, new BindSyncAccountRequest(Seller.ToUpperInvariant()), CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, bound.Status);
        Assert.Equal(Seller, harness.Ebay.BoundUsername);
        Assert.Equal(Seller, harness.Ebay.ExternalAccountId);

        ChannelAccount second = new() { OrganizationId = harness.Org, Channel = "eBay", DisplayName = "Second eBay" };
        harness.Db.ChannelAccounts.Add(second);
        await harness.Db.SaveChangesAsync();
        AppResult<SyncOverviewDto> twice = await harness.Hub.BindAsync(second.Id, new BindSyncAccountRequest(Seller), CancellationToken.None);
        Assert.Equal(AppResultStatus.Conflict, twice.Status);
    }

    [Fact]
    public async Task The_overview_says_what_would_fix_each_account()
    {
        Harness harness = NewHarness();
        (Guid agent, _) = await harness.PairAsync();
        await harness.HeartbeatAsync(agent);

        SyncOverviewDto before = await harness.Hub.OverviewAsync(CancellationToken.None);
        SyncAccountDto ebay = before.Accounts.Single(account => account.ChannelAccountId == harness.Ebay.Id);
        Assert.Equal(SyncAccountHealth.NotBound, ebay.Health);
        Assert.Contains(Seller, ebay.HealthMessage);
        Assert.Equal(SyncAccountHealth.Unsupported, before.Accounts.Single(account => account.ChannelAccountId == harness.Depop.Id).Health);

        await harness.Hub.BindAsync(harness.Ebay.Id, new BindSyncAccountRequest(Seller), CancellationToken.None);
        Assert.Equal(SyncAccountHealth.Ready, (await harness.Hub.OverviewAsync(CancellationToken.None)).Accounts.Single(account => account.ChannelAccountId == harness.Ebay.Id).Health);

        await harness.HeartbeatAsync(agent, signedInAs: "someone_else");
        SyncAccountDto elsewhere = (await harness.Hub.OverviewAsync(CancellationToken.None)).Accounts.Single(account => account.ChannelAccountId == harness.Ebay.Id);
        Assert.Equal(SyncAccountHealth.NoBrowser, elsewhere.Health);
        Assert.Contains("someone_else", elsewhere.HealthMessage);

        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(SyncAccountHealth.NoBrowser, (await harness.Hub.OverviewAsync(CancellationToken.None)).Accounts.Single(account => account.ChannelAccountId == harness.Ebay.Id).Health);
    }

    [Fact]
    public async Task A_schedule_cannot_press_a_marketplace_harder_than_it_allows()
    {
        Harness harness = NewHarness();

        Assert.Equal(AppResultStatus.Validation, (await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(2, null), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Validation, (await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(null, 30), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Validation, (await harness.Hub.UpdateScheduleAsync(harness.Depop.Id, new UpdateSyncScheduleRequest(10, null), CancellationToken.None)).Status);

        Assert.Equal(AppResultStatus.Ok, (await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(5, 60), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Ok, (await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(null, null), CancellationToken.None)).Status);
        Assert.Null(harness.Ebay.SalesScanEveryMinutes);
    }

    [Fact]
    public async Task A_kill_switch_stops_every_browser_on_its_next_heartbeat()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(10, null), CancellationToken.None);
        harness.Options.Platforms["eBay"] = new SyncPlatformSwitch { Enabled = false, DisabledReason = "eBay changed its orders page; a fix is on the way." };

        AgentHeartbeatDto beat = await harness.HeartbeatAsync(agent);
        Assert.Empty(beat.Work);
        AgentPlatformPolicyDto policy = beat.Platforms.Single(platform => platform.Platform == "eBay");
        Assert.False(policy.Enabled);
        Assert.True(beat.Platforms.Single(platform => platform.Platform == "Vinted").Enabled);
        Assert.Equal("eBay changed its orders page; a fix is on the way.", policy.DisabledReason);
        Assert.Equal(AppErrorCode.SyncUnavailable, (await harness.StartAsync(agent, SyncLoopKind.Sales)).Code);
        Assert.Equal(SyncAccountHealth.Disabled, (await harness.Hub.OverviewAsync(CancellationToken.None)).Accounts.Single(account => account.ChannelAccountId == harness.Ebay.Id).Health);
    }

    // ── Scan requests ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Asking_twice_queues_one_scan_and_names_what_it_skipped()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();

        RequestScanResultDto first = (await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Everything), CancellationToken.None)).Value!;
        Assert.Equal([SyncLoopKind.Sales, SyncLoopKind.Listings], first.Requests.Select(request => request.Loop).ToArray());
        SkippedScanDto skipped = Assert.Single(first.Skipped);
        Assert.Equal(harness.Depop.Id, skipped.ChannelAccountId);
        Assert.Contains("Depop", skipped.Reason);

        RequestScanResultDto second = (await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Sales, harness.Ebay.Id), CancellationToken.None)).Value!;
        Assert.Equal(first.Requests[0].Id, Assert.Single(second.Requests).Id);
        Assert.Equal(2, await harness.Db.SyncScanRequests.CountAsync());

        // Asked-for work comes first, before anything a schedule would run.
        AgentHeartbeatDto beat = await harness.HeartbeatAsync(agent);
        Assert.All(beat.Work, work => Assert.Equal(SyncTrigger.Requested, work.Trigger));
        Assert.Equal(2, beat.Work.Count);
    }

    [Fact]
    public async Task History_past_the_ledger_is_refused_with_the_reason()
    {
        Harness harness = NewHarness();
        await harness.ReadyAgentAsync();
        DateTimeOffset now = harness.Clock.GetUtcNow();

        AppResult<RequestScanResultDto> tooOld = await harness.Hub.RequestScanAsync(
            new RequestScanRequest(SyncScanScope.History, harness.Ebay.Id, From: now.AddDays(-120)), CancellationToken.None);
        Assert.Equal(AppResultStatus.Validation, tooOld.Status);
        Assert.Contains("90 days. For older sales, import the order report.", tooOld.Message);

        Assert.Equal(AppResultStatus.Validation, (await harness.Hub.RequestScanAsync(
            new RequestScanRequest(SyncScanScope.History, harness.Ebay.Id), CancellationToken.None)).Status);

        SyncScanRequestDto history = Assert.Single((await harness.Hub.RequestScanAsync(
            new RequestScanRequest(SyncScanScope.History, harness.Ebay.Id, From: now.AddDays(-60)), CancellationToken.None)).Value!.Requests);
        Assert.Equal((SyncLoopKind.Sales, now.AddDays(-60)), (history.Loop, history.From));
    }

    [Fact]
    public async Task A_scan_nobody_picks_up_expires_and_says_so()
    {
        Harness harness = NewHarness();
        (Guid agent, _) = await harness.PairAsync();
        await harness.HeartbeatAsync(agent);
        await harness.Hub.BindAsync(harness.Ebay.Id, new BindSyncAccountRequest(Seller), CancellationToken.None);
        SyncScanRequestDto request = Assert.Single((await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Sales, harness.Ebay.Id), CancellationToken.None)).Value!.Requests);

        harness.Clock.Advance(TimeSpan.FromHours(harness.Options.ScanRequestExpiryHours + 1));
        await harness.HeartbeatAsync(agent, signedInAs: null);

        SyncScanRequest expired = await harness.Db.SyncScanRequests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(SyncScanStatus.Expired, expired.Status);
        Assert.Contains("24 hours", expired.Message);
    }

    // ── Runs ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_sales_run_lands_orders_through_the_import_pipeline_and_answers_the_request()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        SyncScanRequestDto asked = Assert.Single((await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Sales, harness.Ebay.Id), CancellationToken.None)).Value!.Requests);

        AgentRunDto run = (await harness.StartAsync(agent, SyncLoopKind.Sales, asked.Id)).Value!;
        Assert.Equal(SyncScanStatus.Running, (await harness.Db.SyncScanRequests.SingleAsync()).Status);

        AgentRunBatchResultDto batch = (await harness.As(agent).AddBatchAsync(run.RunId,
            new AgentRunBatchRequest(Orders: [EbayOrder("26-15152-72360", "377273002938"), EbayOrder("07-15174-93677", "377251512872", fees: null)],
                Issues: [new AgentRunIssueInput("An order had no readable date.", "99-00000-00000", Rejected: true)]),
            CancellationToken.None)).Value!;
        Assert.Equal(2, batch.Created);

        SyncRunDto done = (await harness.As(agent).CompleteRunAsync(run.RunId,
            new CompleteAgentRunRequest(Cursor: "2026-09-18T07:48:00Z"), CancellationToken.None)).Value!;
        Assert.Equal("CompletedWithIssues", done.Status);
        Assert.Equal("2 new orders. 1 could not be read.", done.Summary);
        Assert.Equal("eBay sales scan", done.FormatLabel);
        Assert.Equal("Requested", done.Trigger);
        Assert.Equal("Firefox on macOS", done.AgentName);

        List<PlatformOrder> orders = await harness.Db.PlatformOrders.OrderBy(order => order.ExternalOrderId).ToListAsync();
        Assert.All(orders, order => Assert.Equal(harness.Ebay.Id, order.ChannelAccountId));
        Assert.True(orders[0].FeesPending);
        Assert.False(orders[1].FeesPending);

        SyncLoop loop = await harness.Db.SyncLoops.SingleAsync();
        Assert.Equal(("2026-09-18T07:48:00Z", 0), (loop.Cursor, loop.ConsecutiveFailures));
        Assert.Null(loop.LeaseAgentId);
        SyncScanRequest answered = await harness.Db.SyncScanRequests.SingleAsync();
        Assert.Equal((SyncScanStatus.Succeeded, done.Summary), (answered.Status, answered.Message));
    }

    [Fact]
    public async Task A_cancelled_order_is_set_aside_unless_it_already_has_sales()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        AgentRunDto first = (await harness.StartAsync(agent, SyncLoopKind.Sales)).Value!;
        await harness.As(agent).AddBatchAsync(first.RunId,
            new AgentRunBatchRequest(Orders: [EbayOrder("26-15152-72360", "377273002938"), EbayOrder("07-15174-93677", "377251512872")]),
            CancellationToken.None);
        await harness.As(agent).CompleteRunAsync(first.RunId, new CompleteAgentRunRequest(), CancellationToken.None);

        // One of the two has since been turned into sales.
        PlatformOrder settled = await harness.Db.PlatformOrders.SingleAsync(order => order.ExternalOrderId == "07-15174-93677");
        settled.Status = PlatformOrderStatus.Reconciled;
        await harness.Db.SaveChangesAsync();

        // The marketplace now shows both cancelled, and one this organization never imported.
        AgentRunDto next = (await harness.StartAsync(agent, SyncLoopKind.Sales)).Value!;
        AgentRunBatchResultDto batch = (await harness.As(agent).AddBatchAsync(next.RunId,
            new AgentRunBatchRequest(CancelledOrderIds: ["26-15152-72360", "07-15174-93677", "11-11111-11111"]), CancellationToken.None)).Value!;
        SyncRunDto done = (await harness.As(agent).CompleteRunAsync(next.RunId, new CompleteAgentRunRequest(), CancellationToken.None)).Value!;

        Assert.Equal(1, batch.Updated);
        Assert.Equal(PlatformOrderStatus.Ignored, (await harness.Db.PlatformOrders.SingleAsync(order => order.ExternalOrderId == "26-15152-72360")).Status);
        Assert.Equal(PlatformOrderStatus.Reconciled, (await harness.Db.PlatformOrders.SingleAsync(order => order.ExternalOrderId == "07-15174-93677")).Status);
        SyncRunIssue issue = Assert.Single(await harness.Db.SyncRunIssues.Where(candidate => candidate.SyncRunId == next.RunId).ToListAsync());
        Assert.Equal("07-15174-93677", issue.ExternalReference);
        Assert.StartsWith("eBay cancelled this order after it was recorded as a sale.", issue.Message);
        Assert.Equal("CompletedWithIssues", done.Status);

        // Cancellations are a sales run's to deliver.
        AgentRunDto listings = (await harness.StartAsync(agent, SyncLoopKind.Listings)).Value!;
        Assert.Equal(AppResultStatus.Validation, (await harness.As(agent).AddBatchAsync(listings.RunId,
            new AgentRunBatchRequest(CancelledOrderIds: ["26-15152-72360"]), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Vinted_syncs_both_loops_and_reaches_back_through_its_whole_history()
    {
        Harness harness = NewHarness();
        ChannelAccount vinted = new() { OrganizationId = harness.Org, Channel = "Vinted", DisplayName = "Overhaul Vinted" };
        harness.Db.ChannelAccounts.Add(vinted);
        await harness.Db.SaveChangesAsync();

        SyncOverviewDto overview = await harness.Hub.OverviewAsync(CancellationToken.None);
        SyncAccountDto account = overview.Accounts.Single(candidate => candidate.ChannelAccountId == vinted.Id);
        Assert.True(account.Supported);
        Assert.True(account.SupportsHistory);
        Assert.Equal([SyncLoopKind.Sales, SyncLoopKind.Listings], account.SupportedLoops);
        Assert.Equal(SyncAccountHealth.NotBound, account.Health);
        Assert.Equal(3650, overview.Platforms.Single(platform => platform.Platform == "Vinted").HistoryDays);
    }

    [Fact]
    public async Task One_browser_at_a_time_holds_a_loop_and_a_lapsed_lease_is_taken_over_cleanly()
    {
        Harness harness = NewHarness();
        Guid laptop = await harness.ReadyAgentAsync("Firefox");
        Guid miniPc = await harness.ReadyAgentAsync("Chrome");
        await harness.HeartbeatAsync(miniPc);

        AgentRunDto first = (await harness.StartAsync(laptop, SyncLoopKind.Listings)).Value!;
        Assert.Equal(AppErrorCode.SyncLeaseHeld, (await harness.StartAsync(miniPc, SyncLoopKind.Listings)).Code);
        Assert.DoesNotContain((await harness.HeartbeatAsync(miniPc)).Work, work => work.Loop == SyncLoopKind.Listings);

        // The laptop closes mid-run; once its lease lapses the mini-PC takes over.
        harness.Clock.Advance(TimeSpan.FromMinutes(harness.Options.LeaseMinutes + 1));
        AppResult<AgentRunDto> takeover = await harness.StartAsync(miniPc, SyncLoopKind.Listings);
        Assert.Equal(AppResultStatus.Ok, takeover.Status);

        SyncRun abandoned = await harness.Db.SyncRuns.SingleAsync(run => run.Id == first.RunId);
        Assert.Equal((SyncRunStatus.Failed, SyncSweep.AbandonedMessage), (abandoned.Status, abandoned.Summary));
        AppResult<AgentRunBatchResultDto> late = await harness.As(laptop).AddBatchAsync(first.RunId,
            new AgentRunBatchRequest(Listings: [new IngestPlatformListingInput("377296454991", "Gucci Oxford Shirt", 249.99m, PlatformListingStatus.Active)]),
            CancellationToken.None);
        Assert.Equal(AppErrorCode.SyncRunFinished, late.Code);
        Assert.Empty(await harness.Db.PlatformListings.ToListAsync());
    }

    [Fact]
    public async Task A_browser_that_vanishes_mid_run_is_closed_out_by_the_next_heartbeat()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        SyncScanRequestDto asked = Assert.Single((await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Sales, harness.Ebay.Id), CancellationToken.None)).Value!.Requests);
        AgentRunDto run = (await harness.StartAsync(agent, SyncLoopKind.Sales, asked.Id)).Value!;

        harness.Clock.Advance(TimeSpan.FromMinutes(harness.Options.LeaseMinutes + 1));
        await harness.HeartbeatAsync(agent, signedInAs: null);

        Assert.Equal(SyncRunStatus.Failed, (await harness.Db.SyncRuns.SingleAsync(item => item.Id == run.RunId)).Status);
        Assert.Equal(SyncScanStatus.Failed, (await harness.Db.SyncScanRequests.SingleAsync()).Status);
        SyncLoop loop = await harness.Db.SyncLoops.SingleAsync();
        Assert.Equal((1, (Guid?)null), (loop.ConsecutiveFailures, loop.LeaseRunId));
    }

    [Fact]
    public async Task A_failed_run_says_what_to_do_and_its_schedule_backs_off()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(10, null), CancellationToken.None);
        AgentRunDto run = (await harness.StartAsync(agent, SyncLoopKind.Sales)).Value!;

        SyncRunDto failed = (await harness.As(agent).FailRunAsync(run.RunId,
            new FailAgentRunRequest(SyncFailureKind.Challenge, "Captcha on /sh/ord."), CancellationToken.None)).Value!;
        Assert.Equal("Failed", failed.Status);
        Assert.StartsWith("eBay wants to confirm a person is there.", failed.Summary);

        SyncLoop loop = await harness.Db.SyncLoops.SingleAsync();
        Assert.Equal(1, loop.ConsecutiveFailures);
        Assert.Equal(loop.LastStartedAt!.Value.AddMinutes(20), SyncWorkPlanner.DueAt(10, loop));
        loop.ConsecutiveFailures = 9;
        Assert.Equal(loop.LastStartedAt!.Value.AddMinutes(80), SyncWorkPlanner.DueAt(10, loop));
    }

    [Fact]
    public async Task A_browser_writes_only_what_the_person_who_paired_it_may_write()
    {
        Harness harness = NewHarness(HaulScoutPermissions.ManageListings);
        Guid agent = await harness.ReadyAgentAsync();

        Assert.Equal(AppErrorCode.SyncNotPermitted, (await harness.StartAsync(agent, SyncLoopKind.Sales)).Code);
        Assert.Equal(AppResultStatus.Ok, (await harness.StartAsync(agent, SyncLoopKind.Listings)).Status);
    }

    [Fact]
    public async Task Bringing_in_sales_takes_the_finance_permission()
    {
        Harness harness = NewHarness(HaulScoutPermissions.ManageListings);
        await harness.ReadyAgentAsync();

        RequestScanResultDto result = (await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Everything, harness.Ebay.Id), CancellationToken.None)).Value!;
        Assert.Equal(SyncLoopKind.Listings, Assert.Single(result.Requests).Loop);
        Assert.Contains(result.Skipped, skipped => skipped.Reason.Contains("cannot bring in sales"));

        Assert.Equal(AppResultStatus.Validation, (await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(10, null), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.Ok, (await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(null, 240), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_complete_listings_snapshot_is_recorded_as_one()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        IngestPlatformListingInput[] live =
        [
            new("377296454991", "Gucci Oxford Shirt", 249.99m, PlatformListingStatus.Active),
            new("377296454992", "Levi's 501", 45m, PlatformListingStatus.Active),
        ];
        AgentRunDto run = (await harness.StartAsync(agent, SyncLoopKind.Listings)).Value!;
        await harness.As(agent).AddBatchAsync(run.RunId, new AgentRunBatchRequest(Listings: live), CancellationToken.None);

        SyncRunDto done = (await harness.As(agent).CompleteRunAsync(run.RunId, new CompleteAgentRunRequest(CompleteSnapshot: true), CancellationToken.None)).Value!;

        Assert.True(done.IsCompleteSnapshot);
        Assert.Equal("2 new listings.", done.Summary);
        Assert.All(await harness.Db.PlatformListings.ToListAsync(), listing => Assert.Equal(harness.Ebay.Id, listing.ChannelAccountId));

        // The same listings again: nothing changed, and the summary says that in so many words.
        AgentRunDto again = (await harness.StartAsync(agent, SyncLoopKind.Listings)).Value!;
        await harness.As(agent).AddBatchAsync(again.RunId, new AgentRunBatchRequest(Listings: live), CancellationToken.None);
        SyncRunDto quiet = (await harness.As(agent).CompleteRunAsync(again.RunId, new CompleteAgentRunRequest(CompleteSnapshot: true), CancellationToken.None)).Value!;
        Assert.Equal("No changes — 2 listings checked.", quiet.Summary);

        // The browser learns where the loop stands from its heartbeat, whichever browser ran it.
        AgentLoopDto listings = (await harness.HeartbeatAsync(agent)).Accounts
            .Single(account => account.ChannelAccountId == harness.Ebay.Id).Loops
            .Single(loop => loop.Loop == SyncLoopKind.Listings);
        Assert.Equal((harness.Clock.GetUtcNow(), (string?)null, false), (listings.LastSucceededAt, listings.LastError, listings.Running));
    }

    // ── Browsers ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_device_token_rotates_weekly_without_ever_locking_the_browser_out()
    {
        Harness harness = NewHarness();
        (Guid agentId, string original) = await harness.PairAsync();
        EfSyncAgentStore store = new(harness.Db);

        Assert.Null((await harness.HeartbeatAsync(agentId)).NewToken);
        harness.Clock.Advance(TimeSpan.FromDays(harness.Options.TokenRotationDays + 1));
        string? rotated = (await harness.HeartbeatAsync(agentId)).NewToken;
        Assert.NotNull(rotated);

        // Until the browser is seen using the new token, the old one still works…
        AgentTokenMatch? old = await store.FindAgentByTokenAsync(SyncSecrets.HashToken(original), CancellationToken.None);
        Assert.True(old!.UsedPreviousToken);

        // …and if the answer carrying the new one was lost, the next heartbeat simply issues another.
        string? reissued = (await harness.As(agentId, usedPreviousToken: true).HeartbeatAsync(
            new AgentHeartbeatRequest("1.0.0", "Firefox", []), CancellationToken.None)).Value!.NewToken;
        Assert.NotNull(reissued);
        Assert.NotNull(await store.FindAgentByTokenAsync(SyncSecrets.HashToken(original), CancellationToken.None));

        // Once it uses the new token, the old one is gone for good.
        await harness.HeartbeatAsync(agentId);
        Assert.Null(await store.FindAgentByTokenAsync(SyncSecrets.HashToken(original), CancellationToken.None));
        Assert.NotNull(await store.FindAgentByTokenAsync(SyncSecrets.HashToken(reissued!), CancellationToken.None));
    }

    [Fact]
    public async Task Work_a_browser_cannot_start_says_why_and_is_not_offered_again()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        await harness.Hub.UpdateScheduleAsync(harness.Ebay.Id, new UpdateSyncScheduleRequest(10, null), CancellationToken.None);
        SyncScanRequestDto asked = Assert.Single((await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Sales, harness.Ebay.Id), CancellationToken.None)).Value!.Requests);

        // The browser's sync tab was closed before it could start: nothing was read, no run opened.
        AppResult reported = await harness.As(agent).RecordWorkFailureAsync(
            new AgentWorkFailureRequest(harness.Ebay.Id, SyncLoopKind.Sales, SyncFailureKind.Interrupted, ScanRequestId: asked.Id), CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, reported.Status);
        SyncScanRequest request = await harness.Db.SyncScanRequests.SingleAsync();
        Assert.Equal(SyncScanStatus.Failed, request.Status);
        Assert.StartsWith("The sync tab was closed", request.Message);
        Assert.Empty(await harness.Db.SyncRuns.ToListAsync());

        SyncLoop loop = await harness.Db.SyncLoops.SingleAsync(candidate => candidate.Kind == SyncLoopKind.Sales);
        Assert.Equal(1, loop.ConsecutiveFailures);
        Assert.StartsWith("The sync tab was closed", loop.LastError);

        // Neither the request nor the schedule comes straight back on the next heartbeat.
        Assert.DoesNotContain((await harness.HeartbeatAsync(agent)).Work, work => work.Loop == SyncLoopKind.Sales);
    }

    [Fact]
    public async Task A_scan_that_needs_a_sign_in_waits_for_it_rather_than_failing()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        SyncScanRequestDto asked = Assert.Single((await harness.Hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Sales, harness.Ebay.Id), CancellationToken.None)).Value!.Requests);

        // The browser went to scan and found itself signed out of eBay. It says so on its heartbeat…
        AppResult reported = await harness.As(agent).RecordWorkFailureAsync(
            new AgentWorkFailureRequest(harness.Ebay.Id, SyncLoopKind.Sales, SyncFailureKind.SignedOut, ScanRequestId: asked.Id), CancellationToken.None);
        Assert.Equal(AppResultStatus.Ok, reported.Status);
        Assert.Empty((await harness.HeartbeatAsync(agent, signedInAs: null)).Work);

        // …the scan waits, with the schedule untouched, and runs as soon as the browser is signed in again.
        Assert.Equal(SyncScanStatus.Queued, (await harness.Db.SyncScanRequests.SingleAsync()).Status);
        Assert.All(await harness.Db.SyncLoops.ToListAsync(), loop => Assert.Equal(0, loop.ConsecutiveFailures));
        AgentWorkDto work = Assert.Single((await harness.HeartbeatAsync(agent)).Work);
        Assert.Equal((SyncTrigger.Requested, asked.Id), (work.Trigger, work.ScanRequestId));
    }

    [Fact]
    public async Task A_browser_can_disconnect_itself()
    {
        Harness harness = NewHarness();
        (Guid agent, string token) = await harness.PairAsync();

        Assert.Equal(AppResultStatus.Ok, (await harness.As(agent).DisconnectAsync(CancellationToken.None)).Status);

        SyncAgent gone = await harness.Db.SyncAgents.SingleAsync();
        Assert.NotNull(gone.RevokedAt);
        Assert.Equal(harness.User, gone.RevokedByUserId);
        Assert.NotNull(await new EfSyncAgentStore(harness.Db).FindAgentByTokenAsync(SyncSecrets.HashToken(token), CancellationToken.None));
    }

    [Fact]
    public async Task Disconnecting_a_browser_closes_what_it_was_running()
    {
        Harness harness = NewHarness();
        Guid agent = await harness.ReadyAgentAsync();
        AgentRunDto run = (await harness.StartAsync(agent, SyncLoopKind.Sales)).Value!;

        Assert.Equal(AppResultStatus.Ok, (await harness.Hub.RevokeAgentAsync(agent, CancellationToken.None)).Status);

        Assert.NotNull((await harness.Db.SyncAgents.SingleAsync()).RevokedAt);
        Assert.Equal(SyncRunStatus.Failed, (await harness.Db.SyncRuns.SingleAsync(item => item.Id == run.RunId)).Status);
        Assert.Null((await harness.Db.SyncLoops.SingleAsync()).LeaseAgentId);
        SyncAgentDto listed = (await harness.Hub.OverviewAsync(CancellationToken.None)).Agents.Single();
        Assert.True(listed.Revoked);
        Assert.False(listed.Online);
    }
}
