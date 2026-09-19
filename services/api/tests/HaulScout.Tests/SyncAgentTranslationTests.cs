using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Sync.Agents;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Sync;

namespace HaulScout.Tests;

/// <summary>
/// The sync store against Postgres: the lease is only safe if the database, not the C#, decides which
/// of two racing browsers wins — something the in-memory provider cannot show.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SyncAgentTranslationTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class EveryPermission : Application.Organizations.IOrganizationAuthorizer
    {
        public Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task RequireOwner(Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task Two_browsers_racing_for_a_lease_cannot_both_win()
    {
        (HaulScoutDbContext seed, Guid org, _) = await postgres.NewOrganizationAsync();
        ChannelAccount account = new() { OrganizationId = org, Channel = "eBay", DisplayName = "eBay" };
        await using (seed)
        {
            seed.ChannelAccounts.Add(account);
            seed.SyncLoops.Add(new SyncLoop { OrganizationId = org, ChannelAccountId = account.Id, Kind = SyncLoopKind.Sales });
            await seed.SaveChangesAsync();
        }

        await using HaulScoutDbContext laptopDb = postgres.NewContext(org);
        await using HaulScoutDbContext miniPcDb = postgres.NewContext(org);
        EfSyncAgentStore laptop = new(laptopDb);
        EfSyncAgentStore miniPc = new(miniPcDb);
        SyncLoop seenByLaptop = (await laptop.FindLoopAsync(org, account.Id, SyncLoopKind.Sales, CancellationToken.None))!;
        SyncLoop seenByMiniPc = (await miniPc.FindLoopAsync(org, account.Id, SyncLoopKind.Sales, CancellationToken.None))!;

        Take(seenByLaptop, Guid.NewGuid());
        Take(seenByMiniPc, Guid.NewGuid());

        Assert.True(await laptop.TrySaveChangesAsync(CancellationToken.None));
        Assert.False(await miniPc.TrySaveChangesAsync(CancellationToken.None));
        Assert.Empty(miniPcDb.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Two_browsers_starting_an_accounts_first_run_cannot_both_create_its_loop()
    {
        (HaulScoutDbContext seed, Guid org, _) = await postgres.NewOrganizationAsync();
        ChannelAccount account = new() { OrganizationId = org, Channel = "eBay", DisplayName = "eBay" };
        await using (seed)
        {
            seed.ChannelAccounts.Add(account);
            await seed.SaveChangesAsync();
        }

        await using HaulScoutDbContext firstDb = postgres.NewContext(org);
        await using HaulScoutDbContext secondDb = postgres.NewContext(org);
        EfSyncAgentStore first = new(firstDb);
        EfSyncAgentStore second = new(secondDb);
        first.Add(new SyncLoop { OrganizationId = org, ChannelAccountId = account.Id, Kind = SyncLoopKind.Listings });
        second.Add(new SyncLoop { OrganizationId = org, ChannelAccountId = account.Id, Kind = SyncLoopKind.Listings });

        Assert.True(await first.TrySaveChangesAsync(CancellationToken.None));
        Assert.False(await second.TrySaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_hub_reads_translate()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using (db)
        {
            ChannelAccount ebay = new() { OrganizationId = org, Channel = "eBay", DisplayName = "eBay", BoundUsername = "seller", BoundAt = DateTimeOffset.UtcNow };
            SyncAgent agent = new()
            {
                OrganizationId = org,
                Name = "Office mini-PC",
                Browser = "Firefox",
                ExtensionVersion = "1.0.0",
                PairedByUserId = user,
                PairedAt = DateTimeOffset.UtcNow,
                TokenHash = SyncSecrets.HashToken(SyncSecrets.NewToken()),
                TokenIssuedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            db.ChannelAccounts.Add(ebay);
            db.SyncAgents.Add(agent);
            db.SyncAgentMarketplaces.Add(new SyncAgentMarketplace
            {
                OrganizationId = org,
                SyncAgentId = agent.Id,
                Platform = "eBay",
                AccessGranted = true,
                SignedIn = true,
                Username = "seller",
                ObservedAt = DateTimeOffset.UtcNow,
            });
            db.SyncScanRequests.Add(new SyncScanRequest
            {
                OrganizationId = org,
                ChannelAccountId = ebay.Id,
                Loop = SyncLoopKind.Listings,
                Status = SyncScanStatus.Succeeded,
                RequestedByUserId = user,
                RequestedAt = DateTimeOffset.UtcNow.AddHours(-1),
                FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-50),
            });
            await db.SaveChangesAsync();

            SyncHubService hub = new(new EfSyncAgentStore(db), new StubCurrentUser(org, user), new EveryPermission(), TimeProvider.System, Options.Create(new SyncAgentOptions()));
            RequestScanResultDto requested = (await hub.RequestScanAsync(new RequestScanRequest(SyncScanScope.Sales), CancellationToken.None)).Value!;
            SyncOverviewDto overview = await hub.OverviewAsync(CancellationToken.None);

            Assert.Single(requested.Requests);
            SyncAccountDto account = Assert.Single(overview.Accounts);
            Assert.Equal(SyncAccountHealth.Ready, account.Health);
            Assert.Equal("Ready on Office mini-PC.", account.HealthMessage);
            Assert.Equal(2, overview.Requests.Count);
            Assert.Equal("Operator", Assert.Single(overview.Agents).PairedBy);
        }
    }

    private static void Take(SyncLoop loop, Guid agentId)
    {
        loop.LeaseAgentId = agentId;
        loop.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        loop.Version = Guid.NewGuid();
    }
}
