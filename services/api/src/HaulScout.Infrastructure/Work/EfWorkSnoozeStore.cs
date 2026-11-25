using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Work;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Work;

public sealed class EfWorkSnoozeStore(HaulScoutDbContext db) : IWorkSnoozeStore
{
    public async Task<IReadOnlyList<WorkSnooze>> ListActiveAsync(Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.WorkSnoozes.AsNoTracking()
            .Where(snooze => snooze.OrganizationId == organizationId && (snooze.Until == null || snooze.Until > now))
            .ToListAsync(cancellationToken);

    public Task<WorkSnooze?> FindAsync(Guid organizationId, string taskKey, CancellationToken cancellationToken) =>
        db.WorkSnoozes.FirstOrDefaultAsync(snooze => snooze.OrganizationId == organizationId && snooze.TaskKey == taskKey, cancellationToken);

    public void Add(WorkSnooze snooze) => db.WorkSnoozes.Add(snooze);

    public void Remove(WorkSnooze snooze) => db.WorkSnoozes.Remove(snooze);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
