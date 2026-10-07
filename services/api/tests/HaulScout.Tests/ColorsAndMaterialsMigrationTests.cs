using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using HaulScout.Application.Common;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// The colour and material data already in production, put through the migration that turns them into lists: a
/// database migrated to the step before, seeded the old way, then brought up to date.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ColorsAndMaterialsMigrationTests(PostgresFixture postgres)
{
    private const string StepBefore = "20261004230404_ClearPlaceholderAttributeValues";

    [Fact]
    public async Task Text_colors_and_materials_become_lists_in_the_order_written()
    {
        string database = $"migration_{Guid.NewGuid():N}";
        await using (NpgsqlConnection admin = new(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using NpgsqlCommand create = new($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = Context(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString, org);
        IMigrator migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(StepBefore);
        db.Organizations.Add(new Organization { Id = org, Name = "Migration org" });
        await db.SaveChangesAsync();
        Guid twoColors = Guid.NewGuid(), unknownColor = Guid.NewGuid(), blank = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "InventoryItems" ("Id", "OrganizationId", "Title", "Status", "OwnershipType", "FunctionalStatus", "CompletenessStatus",
                "AuthenticityStatus", "PackagingStatus", "DimensionUnit", "WeightUnit", "IsArchived", "IsBundle", "Quantity", "CreatedAt", "UpdatedAt",
                "Color", "AttributesJson")
            VALUES
                ({0}, {3}, 'Scarf', 'Listed', 'Owned', 'Unknown', 'Unknown', 'NotAssessed', 'NotApplicable', 'Inches', 'Ounces', false, false, 1, now(), now(),
                 'Grey, Off-White, Red', CAST({4} AS jsonb)),
                ({1}, {3}, 'Lamp', 'Listed', 'Owned', 'Unknown', 'Unknown', 'NotAssessed', 'NotApplicable', 'Inches', 'Ounces', false, false, 1, now(), now(),
                 'Chartreuse', CAST({5} AS jsonb)),
                ({2}, {3}, 'Mug', 'Draft', 'Owned', 'Unknown', 'Unknown', 'NotAssessed', 'NotApplicable', 'Inches', 'Ounces', false, false, 1, now(), now(),
                 NULL, NULL)
            """,
            twoColors,
            unknownColor,
            blank,
            org,
            """{"material": "Linen, cotton and polyester", "department": "Women"}""",
            """{"material": ["Brass"]}""");

        await migrator.MigrateAsync();

        db.ChangeTracker.Clear();
        Dictionary<Guid, InventoryItem> items = await db.InventoryItems.ToDictionaryAsync(item => item.Id);
        Assert.Equal(["Gray", "Cream"], items[twoColors].Colors);
        Assert.Equal("""{"material": ["Linen", "Cotton", "Polyester"], "department": "Women"}""", items[twoColors].AttributesJson);
        Assert.Empty(items[unknownColor].Colors);
        Assert.Equal("""{"material": ["Brass"]}""", items[unknownColor].AttributesJson);
        Assert.Empty(items[blank].Colors);
        Assert.Null(items[blank].AttributesJson);
    }

    private static HaulScoutDbContext Context(string connectionString, Guid org)
    {
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        return new HaulScoutDbContext(new DbContextOptionsBuilder<HaulScoutDbContext>().UseNpgsql(connectionString).Options, tenant);
    }
}
