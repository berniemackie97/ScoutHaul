using Microsoft.EntityFrameworkCore;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Common;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// A few operations here cannot be done in one write — deleting a sale has to persist the removal
/// before it can tell whether the order it belonged to is now empty. Two writes with nothing around
/// them means a failure between them leaves the first one standing.
///
/// Against a real Postgres, because the in-memory provider has no transactions to test.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UnitOfWorkTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Everything_inside_commits_together()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        EfUnitOfWork unitOfWork = new(db);

        await unitOfWork.InTransactionAsync(async token =>
        {
            db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "First" });
            await db.SaveChangesAsync(token);
            db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "Second" });
            await db.SaveChangesAsync(token);
            return true;
        }, CancellationToken.None);

        await using HaulScoutDbContext fresh = postgres.NewContext(org);
        Assert.Equal(2, await fresh.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task A_failure_part_way_through_leaves_nothing_behind()
    {
        // The whole point. Without this, the first save stands and the operation is half-done.
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        EfUnitOfWork unitOfWork = new(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.InTransactionAsync<bool>(async token =>
            {
                db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "Written" });
                await db.SaveChangesAsync(token);
                throw new InvalidOperationException("the second step failed");
            }, CancellationToken.None));

        await using HaulScoutDbContext fresh = postgres.NewContext(org);
        Assert.Equal(0, await fresh.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task The_original_failure_is_what_the_caller_sees()
    {
        // Rolling back must not swallow or replace the reason.
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        EfUnitOfWork unitOfWork = new(db);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.InTransactionAsync<bool>(_ => throw new InvalidOperationException("the real reason"),
                CancellationToken.None));

        Assert.Equal("the real reason", error.Message);
    }

    [Fact]
    public async Task An_inner_call_joins_the_transaction_already_running()
    {
        // Nesting must not start a second transaction, which Postgres would refuse — and an inner
        // failure must still roll the outer one back.
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        EfUnitOfWork unitOfWork = new(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.InTransactionAsync<bool>(async outer =>
            {
                db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "Outer" });
                await db.SaveChangesAsync(outer);

                return await unitOfWork.InTransactionAsync<bool>(async inner =>
                {
                    db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "Inner" });
                    await db.SaveChangesAsync(inner);
                    throw new InvalidOperationException("the inner step failed");
                }, outer);
            }, CancellationToken.None));

        await using HaulScoutDbContext fresh = postgres.NewContext(org);
        Assert.Equal(0, await fresh.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task The_value_the_work_returns_comes_back_out()
    {
        (HaulScoutDbContext db, _, _) = await postgres.NewOrganizationAsync();
        EfUnitOfWork unitOfWork = new(db);

        Assert.Equal(42, await unitOfWork.InTransactionAsync(_ => Task.FromResult(42), CancellationToken.None));
    }
}
