using Microsoft.EntityFrameworkCore;
using Npgsql;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>A save that lost a race is recognised whichever way EF hands it back.</summary>
public class SaveConflictsTests
{
    private static DbUpdateException Failed(string sqlState) =>
        new("save failed", new PostgresException("conflict", "ERROR", "ERROR", sqlState));

    [Fact]
    public void A_deadlock_wrapped_as_a_transient_failure_is_a_lost_race()
    {
        InvalidOperationException transient = new("An exception has been raised that is likely due to a transient failure.", Failed(PostgresErrorCodes.DeadlockDetected));

        DbUpdateException? failure = SaveConflicts.Unwrap(transient);

        Assert.NotNull(failure);
        Assert.True(SaveConflicts.LostRace(failure));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
    [InlineData(PostgresErrorCodes.SerializationFailure, true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, false)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, false)]
    public void Only_losing_to_another_session_counts(string sqlState, bool lost) =>
        Assert.Equal(lost, SaveConflicts.LostRace(Failed(sqlState)));

    [Fact]
    public void A_stale_version_is_a_lost_race_and_an_unrelated_failure_is_not_a_save_at_all()
    {
        Assert.True(SaveConflicts.LostRace(new DbUpdateConcurrencyException("stale")));
        Assert.Null(SaveConflicts.Unwrap(new InvalidOperationException("something else")));
    }
}
