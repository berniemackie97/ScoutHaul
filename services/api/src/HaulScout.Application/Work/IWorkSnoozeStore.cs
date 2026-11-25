using HaulScout.Domain.Entities;

namespace HaulScout.Application.Work;

public interface IWorkSnoozeStore
{
    /// <summary>Snoozes still in force at <paramref name="now"/>: no date, or a date still ahead.</summary>
    Task<IReadOnlyList<WorkSnooze>> ListActiveAsync(Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<WorkSnooze?> FindAsync(Guid organizationId, string taskKey, CancellationToken cancellationToken);

    void Add(WorkSnooze snooze);

    void Remove(WorkSnooze snooze);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
