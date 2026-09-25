using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Sync.Agents;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// The sync agent over real HTTP, through the real pipeline, against real Postgres.
///
/// Unit tests hand services a user context that is always resolved, so they cannot see whether an
/// endpoint's gate, authentication scheme and tenant filter actually line up — which is how search
/// once shipped returning nothing to anyone. These drive the API the way the extension will: pair
/// with a code, then act with the device token, and prove the token works where it should and
/// nowhere else.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SyncAgentHttpTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Seller = "overhaultradingcompany";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, string?> previousEnvironment = [];
    private WebApplicationFactory<Program> factory = null!;

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
    }

    public Task InitializeAsync()
    {
        // The API reads its configuration from the environment. A developer's local .env names a real
        // database; pointing its loader at a file that does not exist keeps these tests on the container.
        SetEnvironment("HAULSCOUT_ENV_FILE", Path.Combine(Path.GetTempPath(), $"no-env-{Guid.NewGuid():N}"));
        SetEnvironment("ConnectionStrings__Postgres", postgres.ConnectionString);
        SetEnvironment("Authentication__Supabase__Url", "https://tests.supabase.co");
        SetEnvironment("Database__ApplyMigrationsOnStartup", "false");
        // The photo worker is a background loop against the same database; these tests drive the API, not it.
        SetEnvironment("Sync__Photos__Enabled", "false");
        factory = new ApiFactory();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        foreach ((string key, string? value) in previousEnvironment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public async Task A_paired_browser_syncs_with_its_token_and_nowhere_else()
    {
        (Guid org, Guid account, string code) = await SeedAsync();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage paired = await client.PostAsJsonAsync("/api/sync/agent/pair",
            new { code, browser = "Firefox", extensionVersion = "1.0.0", operatingSystem = "macOS" });
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        JsonElement pairing = await paired.Content.ReadFromJsonAsync<JsonElement>(Json);
        string token = pairing.GetProperty("token").GetString()!;
        Guid agentId = pairing.GetProperty("agentId").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage beat = await client.PostAsJsonAsync("/api/sync/agent/heartbeat", new
        {
            extensionVersion = "1.0.0",
            browser = "Firefox",
            marketplaces = new[] { new { platform = "eBay", accessGranted = true, signedIn = true, username = Seller } },
        });
        Assert.Equal(HttpStatusCode.OK, beat.StatusCode);
        JsonElement heartbeat = await beat.Content.ReadFromJsonAsync<JsonElement>(Json);
        JsonElement work = Assert.Single(heartbeat.GetProperty("work").EnumerateArray());
        Assert.Equal("Sales", work.GetProperty("loop").GetString());

        HttpResponseMessage started = await client.PostAsJsonAsync("/api/sync/agent/runs",
            new { channelAccountId = account, loop = "Sales", trigger = "Scheduled", signedInAs = Seller });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Guid runId = (await started.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("runId").GetGuid();

        HttpResponseMessage batch = await client.PostAsJsonAsync($"/api/sync/agent/runs/{runId}/batches", new
        {
            orders = new[]
            {
                new
                {
                    externalOrderId = "26-15152-72360",
                    orderedAt = new DateTimeOffset(2026, 9, 18, 7, 48, 0, TimeSpan.Zero),
                    itemsSubtotal = 20m,
                    shippingCharged = 6.99m,
                    taxCollected = 1.96m,
                    feeTotal = 4.08m,
                    lines = new[] { new { title = "Sports Game Lot of 4", unitPrice = 20m, externalItemId = "377273002938" } },
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(1, (await batch.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("created").GetInt32());

        HttpResponseMessage completed = await client.PostAsJsonAsync($"/api/sync/agent/runs/{runId}/complete", new { cursor = "2026-09-18T07:48:00Z" });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("Succeeded", (await completed.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("status").GetString());

        await using (HaulScoutDbContext db = postgres.NewContext(org))
        {
            PlatformOrder order = await db.PlatformOrders.SingleAsync();
            Assert.Equal(account, order.ChannelAccountId);
            Assert.Equal(4.08m, order.FeeTotal);
            SyncRun run = await db.SyncRuns.SingleAsync();
            Assert.Equal((SyncSource.Agent, SyncLoopKind.Sales, agentId), (run.Source, run.Loop!.Value, run.SyncAgentId!.Value));
        }

        // The device token opens the agent's endpoints and nothing else.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sync/overview")).StatusCode);

        // A token nobody issued, and then this one once revoked, are refused with a reason the
        // extension can act on.
        HttpClient stranger = factory.CreateClient();
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SyncSecrets.NewToken());
        await AssertRefusedAsync(stranger, "agent_unknown");

        await using (HaulScoutDbContext db = postgres.NewContext(org))
        {
            SyncAgent agent = await db.SyncAgents.SingleAsync();
            agent.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await AssertRefusedAsync(client, "agent_revoked");

        // A person's session token is not a device token, and the agent's endpoints do not take it.
        HttpClient person = factory.CreateClient();
        person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.sig");
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostHeartbeatAsync(person)).StatusCode);
    }

    [Fact]
    public async Task A_code_cannot_be_used_twice_and_a_bad_one_says_so()
    {
        (_, _, string code) = await SeedAsync();
        HttpClient client = factory.CreateClient();
        object body = new { code, browser = "Firefox", extensionVersion = "1.0.0" };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/sync/agent/pair", body)).StatusCode);
        HttpResponseMessage again = await client.PostAsJsonAsync("/api/sync/agent/pair", body);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("sync_pairing_invalid", (await again.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString());
    }

    private async Task<(Guid Org, Guid Account, string Code)> SeedAsync()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using (db)
        {
            db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org, UserId = user, Role = OrganizationRole.Owner });
            ChannelAccount ebay = new()
            {
                OrganizationId = org,
                Channel = "eBay",
                DisplayName = "Overhaul eBay",
                BoundUsername = Seller,
                BoundAt = DateTimeOffset.UtcNow,
                SalesScanEveryMinutes = 10,
            };
            db.ChannelAccounts.Add(ebay);
            string code = SyncSecrets.NewPairingCode();
            db.SyncPairings.Add(new SyncPairing
            {
                OrganizationId = org,
                CodeHash = SyncSecrets.HashPairingCode(SyncSecrets.NormalizePairingCode(code)!),
                CreatedByUserId = user,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            });
            await db.SaveChangesAsync();
            return (org, ebay.Id, code);
        }
    }

    private static Task<HttpResponseMessage> PostHeartbeatAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/sync/agent/heartbeat", new { extensionVersion = "1.0.0", browser = "Firefox", marketplaces = Array.Empty<object>() });

    private static async Task AssertRefusedAsync(HttpClient client, string code)
    {
        HttpResponseMessage refused = await PostHeartbeatAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(code, (await refused.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString());
    }

    private void SetEnvironment(string key, string value)
    {
        previousEnvironment.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }
}
