using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Middleware;

/// <summary>
/// Makes a retried write happen once.
///
/// The order used to be: look for the key, do the work, then write the key down. Two requests
/// carrying the same key both found nothing, both did the work, and the unique index caught only the
/// second bookkeeping row — after a second sale had already been recorded. It protected the record,
/// not the thing the record was about.
///
/// The key is now claimed first. The unique index on (organization, key) decides which request
/// proceeds; the other is told the work is already running. A claim is released if the work fails,
/// so a genuine retry is never locked out by an attempt that went nowhere.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public async Task InvokeAsync(HttpContext context, HaulScoutDbContext db, ICurrentUserContext currentUser)
    {
        if (!SupportedMethods.Contains(context.Request.Method)
            || !context.Request.Path.StartsWithSegments("/api")
            || !context.Request.Headers.TryGetValue("X-Idempotency-Key", out StringValues values)
            || string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            await next(context);
            return;
        }

        Guid organizationId;
        try
        {
            organizationId = currentUser.OrganizationId;
        }
        catch
        {
            await next(context);
            return;
        }

        string key = values.First()!.Trim();

        IdempotencyRecord? existing = await db.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Key == key, context.RequestAborted);

        if (existing is not null)
        {
            if (existing.CompletedAt is null)
            {
                // A request with this key is in flight. Doing the work again is exactly what this
                // exists to prevent, and there is no answer to replay yet, so say so and let the
                // caller ask again.
                await WriteInProgressAsync(context);
                return;
            }

            context.Response.StatusCode = existing.StatusCode;
            if (!string.IsNullOrWhiteSpace(existing.ResponseJson))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(existing.ResponseJson, context.RequestAborted);
            }

            return;
        }

        // Claim the key before doing anything. If another request claims it first the unique index
        // rejects this one, which is the whole point — only one of them gets to do the work.
        IdempotencyRecord claim = new()
        {
            OrganizationId = organizationId,
            Key = key,
            Method = context.Request.Method,
            Path = context.Request.Path + context.Request.QueryString,
            StatusCode = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            CompletedAt = null,
        };

        db.IdempotencyRecords.Add(claim);
        try
        {
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await WriteInProgressAsync(context);
            return;
        }

        // The claim is committed; the handlers below must not see it as pending work of their own.
        db.ChangeTracker.Clear();

        Stream originalBody = context.Response.Body;
        await using MemoryStream buffer = new();
        context.Response.Body = buffer;

        string responseText;
        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            await ReleaseClaimAsync(db, organizationId, key);
            throw;
        }
        finally
        {
            buffer.Position = 0;
            responseText = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync(CancellationToken.None);
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, CancellationToken.None);
            context.Response.Body = originalBody;
        }

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await db.IdempotencyRecords
                .Where(item => item.OrganizationId == organizationId && item.Key == key)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.StatusCode, context.Response.StatusCode)
                        .SetProperty(item => item.ResponseJson, string.IsNullOrWhiteSpace(responseText) ? null : responseText)
                        .SetProperty(item => item.CompletedAt, DateTimeOffset.UtcNow),
                    CancellationToken.None);
            return;
        }

        // The work was refused. Holding the key would mean a corrected retry — the same operation
        // with the price fixed — was answered with someone else's rejection for the next fortnight.
        await ReleaseClaimAsync(db, organizationId, key);
    }

    private static Task ReleaseClaimAsync(HaulScoutDbContext db, Guid organizationId, string key) =>
        db.IdempotencyRecords
            .Where(item => item.OrganizationId == organizationId && item.Key == key && item.CompletedAt == null)
            .ExecuteDeleteAsync(CancellationToken.None);

    private static async Task WriteInProgressAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/problem+json";

        Microsoft.AspNetCore.Mvc.ProblemDetails problem = new()
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Already in progress",
            Detail = "This request is already being processed. Try again in a moment.",
            Instance = context.Request.Path.Value,
        };
        problem.Extensions["code"] = AppErrorCode.RequestInFlight;

        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
