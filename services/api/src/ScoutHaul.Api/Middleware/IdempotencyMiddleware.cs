using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Services;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;
using System.Text;

namespace ScoutHaul.Api.Middleware;

public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public async Task InvokeAsync(HttpContext context, ScoutHaulDbContext db, ICurrentUserContext currentUser)
    {
        if (!SupportedMethods.Contains(context.Request.Method)
            || !context.Request.Path.StartsWithSegments("/api")
            || !context.Request.Headers.TryGetValue("X-Idempotency-Key", out var values)
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
            context.Response.StatusCode = existing.StatusCode;
            if (!string.IsNullOrWhiteSpace(existing.ResponseJson))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(existing.ResponseJson, context.RequestAborted);
            }

            return;
        }

        Stream originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next(context);

        buffer.Position = 0;
        string responseText = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);
        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
        context.Response.Body = originalBody;

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                OrganizationId = organizationId,
                Key = key,
                Method = context.Request.Method,
                Path = context.Request.Path + context.Request.QueryString,
                StatusCode = context.Response.StatusCode,
                ResponseJson = string.IsNullOrWhiteSpace(responseText) ? null : responseText,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(14)
            });

            try
            {
                await db.SaveChangesAsync(context.RequestAborted);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
