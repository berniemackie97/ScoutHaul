using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HaulScout.Infrastructure.Persistence;

/// <summary>How a save that lost a race to another session shows up, however EF hands it back.</summary>
public static class SaveConflicts
{
    /// <summary>
    /// The DbUpdateException behind a failed save. Npgsql marks deadlocks and serialization failures as transient, and
    /// EF's non-retrying strategy wraps those in an InvalidOperationException instead of throwing them directly.
    /// </summary>
    public static DbUpdateException? Unwrap(Exception exception) =>
        exception as DbUpdateException ?? exception.InnerException as DbUpdateException;

    /// <summary>True when another session's write won: a stale version, a deadlock victim or a serialization failure.</summary>
    public static bool LostRace(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure };
}
