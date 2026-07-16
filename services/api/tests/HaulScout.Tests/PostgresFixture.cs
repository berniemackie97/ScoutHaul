using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace HaulScout.Tests;

/// <summary>
/// A real Postgres, started once for the whole suite and migrated with the production migrations.
///
/// The in-memory provider is a dictionary: it enforces no foreign keys and performs no cascade or set-null, so a test
/// there can only assert what the C# already did — never what the database would do. Several behaviours we rely on
/// (an order's lines cascading away, its sales being released rather than deleted) are the database's job, and until
/// now nothing checked them. These tests run against the same engine production uses.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("scouthaul_tests")
        .WithUsername("tests")
        .WithPassword("tests")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using HaulScoutDbContext db = NewContext(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A context scoped to one organization, exactly as the app resolves it per request.</summary>
    public HaulScoutDbContext NewContext(Guid organizationId)
    {
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(organizationId);
        return new HaulScoutDbContext(options, tenant);
    }

    /// <summary>Seeds an organization + user. Each test gets its own org, so the shared container stays isolated.</summary>
    public async Task<(HaulScoutDbContext Db, Guid Org, Guid User)> NewOrganizationAsync()
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        HaulScoutDbContext db = NewContext(org);
        db.Organizations.Add(new Organization { Id = org, Name = $"Org {org:N}" });
        db.Users.Add(new AppUser { Id = user, Email = $"{user:N}@x.com", DisplayName = "Operator" });
        await db.SaveChangesAsync();
        return (db, org, user);
    }
}

/// <summary>One container for every test in the collection — starting Postgres per test class would be glacial.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
