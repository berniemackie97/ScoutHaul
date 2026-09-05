using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using HaulScout.Application.Common;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Common;

internal sealed class EfUnitOfWork(HaulScoutDbContext db) : IUnitOfWork
{
    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        // Already inside one — joining it is what the caller means, and starting a second would throw.
        if (db.Database.CurrentTransaction is not null)
        {
            return await work(cancellationToken);
        }

        // The in-memory provider used by the fast unit tests has no transactions at all. Running the
        // work directly there keeps those tests working; the behaviour that actually matters — that a
        // failure rolls the whole thing back — is covered against a real Postgres.
        if (!db.Database.IsRelational())
        {
            return await work(cancellationToken);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            T result = await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
