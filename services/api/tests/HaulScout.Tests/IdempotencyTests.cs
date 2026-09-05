using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Middleware;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// A retried write must happen once.
///
/// The middleware used to look for the key, do the work, then write the key down. Two requests
/// carrying the same key both found nothing, both did the work, and the unique index caught only the
/// second bookkeeping row — after a second sale had already been recorded. Against a real Postgres,
/// because the collision this relies on is the database's.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IdempotencyTests(PostgresFixture postgres)
{
    private sealed class StubUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static DefaultHttpContext Request(string key)
    {
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/sales";
        context.Request.Headers["X-Idempotency-Key"] = key;
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>Runs the middleware, counting how many times the work underneath it actually ran.</summary>
    private static async Task<HttpContext> RunAsync(
        HaulScoutDbContext db, Guid org, string key, Func<HttpContext, Task> work)
    {
        DefaultHttpContext context = Request(key);
        IdempotencyMiddleware middleware = new(new RequestDelegate(work));
        await middleware.InvokeAsync(context, db, new StubUser(org));
        return context;
    }

    private static Task Succeeds(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return context.Response.WriteAsync("""{"id":"sale-1"}""");
    }

    [Fact]
    public async Task The_work_runs_once_and_the_answer_is_replayed()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        int ran = 0;

        await RunAsync(db, org, "key-1", context => { ran++; return Succeeds(context); });
        HttpContext replay = await RunAsync(db, org, "key-1", context => { ran++; return Succeeds(context); });

        Assert.Equal(1, ran);
        Assert.Equal(200, replay.Response.StatusCode);
    }

    [Fact]
    public async Task A_second_request_arriving_mid_flight_does_not_do_the_work_again()
    {
        // The bug. Both requests passed the "have I seen this key" check, so the sale was recorded
        // twice and the unique index only rejected the second bookkeeping row.
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext second = postgres.NewContext(org);

        int ran = 0;
        HttpContext? overlapping = null;

        await RunAsync(db, org, "key-2", async context =>
        {
            ran++;
            // While the first request is still working, a retry lands on another connection.
            overlapping = await RunAsync(second, org, "key-2", inner => { ran++; return Succeeds(inner); });
            await Succeeds(context);
        });

        Assert.Equal(1, ran);
        Assert.Equal(StatusCodes.Status409Conflict, overlapping!.Response.StatusCode);
    }

    [Fact]
    public async Task A_refused_request_does_not_hold_the_key()
    {
        // Otherwise a corrected retry — the same sale with the price fixed — is answered with the
        // previous rejection for the next fortnight.
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();

        await RunAsync(db, org, "key-3", context =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        Assert.False(await db.IdempotencyRecords.AnyAsync(record => record.Key == "key-3"));

        int ran = 0;
        HttpContext retry = await RunAsync(db, org, "key-3", context => { ran++; return Succeeds(context); });

        Assert.Equal(1, ran);
        Assert.Equal(200, retry.Response.StatusCode);
    }

    [Fact]
    public async Task A_request_that_threw_does_not_hold_the_key()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(db, org, "key-4", _ => throw new InvalidOperationException("boom")));

        Assert.False(await db.IdempotencyRecords.AnyAsync(record => record.Key == "key-4"));
    }

    [Fact]
    public async Task A_completed_record_is_marked_completed()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();

        await RunAsync(db, org, "key-5", Succeeds);

        IdempotencyRecord record = await db.IdempotencyRecords
            .AsNoTracking()
            .SingleAsync(item => item.Key == "key-5");

        Assert.NotNull(record.CompletedAt);
        Assert.Equal(200, record.StatusCode);
        Assert.Contains("sale-1", record.ResponseJson!);
    }

    [Fact]
    public async Task A_request_without_a_key_is_left_alone()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/sales";
        context.Response.Body = new MemoryStream();

        int ran = 0;
        await new IdempotencyMiddleware(new RequestDelegate(_ => { ran++; return Task.CompletedTask; }))
            .InvokeAsync(context, db, new StubUser(org));

        Assert.Equal(1, ran);
        Assert.Empty(await db.IdempotencyRecords.ToListAsync());
    }
}
