using System.Text.Json;
using HaulScout.Application.Common;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Infrastructure.Persistence;

/// <summary>
/// Stages audit entries on the request's DbContext, so they commit with the change they describe rather than in a
/// write of their own.
/// </summary>
public sealed class EfAuditTrail(HaulScoutDbContext db, ICurrentUserContext currentUser) : IAuditTrail
{
    public async Task RecordAsync(string entityName, string action, object details, CancellationToken cancellationToken) =>
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                OrganizationId = currentUser.OrganizationId,
                ActorUserId = currentUser.UserId,
                EntityName = entityName,
                Action = action,
                DetailsJson = JsonSerializer.Serialize(details),
            },
            cancellationToken);
}
