using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>Synced items carrying a marketplace's words, rewritten in the editor's terms by the migration.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SyncedDetailsMigrationTests(PostgresFixture postgres)
{
    private const string StepBefore = "20261007055846_ColorsAndMaterialsAsLists";

    private static async Task<Dictionary<string, InventoryItem>> MigrateAsync(HaulScoutDbContext db, IMigrator migrator)
    {
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        return await db.InventoryItems.ToDictionaryAsync(item => item.Title);
    }

    [Fact]
    public async Task Marketplace_categories_are_refiled_and_an_organizations_own_are_left_alone()
    {
        Guid org = Guid.NewGuid();
        (HaulScoutDbContext db, IMigrator migrator) = await postgres.DatabaseAtAsync(StepBefore, org);
        await using HaulScoutDbContext scope = db;
        string[] filed = ["Tops", "CCG Individual Cards", "Women > Shoes > Heels", "Football-NFL", "Clothing", "Vintage Denim", "  "];
        db.InventoryItems.AddRange(filed.Select(category => new InventoryItem { OrganizationId = org, Title = category, Category = category }));
        db.Add(new OrgReferenceOverride { OrganizationId = org, Kind = "Category", Value = "Vintage Denim", IsCustom = true });
        await db.SaveChangesAsync();

        Dictionary<string, InventoryItem> items = await MigrateAsync(db, migrator);

        Assert.Equal("Clothing", items["Tops"].Category);
        Assert.Equal("Collectibles", items["CCG Individual Cards"].Category);
        Assert.Equal("Shoes", items["Women > Shoes > Heels"].Category);
        Assert.Equal(ItemCategories.Other, items["Football-NFL"].Category);
        Assert.Equal("Clothing", items["Clothing"].Category);
        Assert.Equal("Vintage Denim", items["Vintage Denim"].Category);
        Assert.Equal("  ", items["  "].Category);
    }

    [Fact]
    public async Task Marketplace_conditions_become_listed_ones_and_unreadable_ones_are_kept()
    {
        Guid org = Guid.NewGuid();
        (HaulScoutDbContext db, IMigrator migrator) = await postgres.DatabaseAtAsync(StepBefore, org);
        await using HaulScoutDbContext scope = db;
        string[] written = ["ug", "uln", "Pre-owned - Excellent", "Used - needs major repair/alteration", "Used - LU", "Very good", "Ungraded"];
        db.InventoryItems.AddRange(written.Select(condition => new InventoryItem { OrganizationId = org, Title = condition, Condition = condition }));
        await db.SaveChangesAsync();

        Dictionary<string, InventoryItem> items = await MigrateAsync(db, migrator);

        Assert.Equal("Good", items["ug"].Condition);
        Assert.Equal("Like new", items["uln"].Condition);
        Assert.Equal("Excellent", items["Pre-owned - Excellent"].Condition);
        Assert.Equal("Poor", items["Used - needs major repair/alteration"].Condition);
        Assert.Equal("Excellent", items["Used - LU"].Condition);
        Assert.Equal("Very good", items["Very good"].Condition);
        Assert.Equal("Ungraded", items["Ungraded"].Condition);
    }

    [Fact]
    public async Task A_size_without_a_system_gets_one_where_its_category_says_which()
    {
        Guid org = Guid.NewGuid();
        (HaulScoutDbContext db, IMigrator migrator) = await postgres.DatabaseAtAsync(StepBefore, org);
        await using HaulScoutDbContext scope = db;
        (string Title, string Category, string Size)[] seeded =
        [
            ("Dress", "Dresses", "L / US 12-14"), ("Tee", "Tops", "XXL"), ("Hat", "Clothing", "OS"), ("Jeans", "Jeans", "14"),
            ("Heels", "Shoes", "7"), ("Belt", "Clothing", "W36"),
        ];
        db.InventoryItems.AddRange(seeded.Select(item => new InventoryItem { OrganizationId = org, Title = item.Title, Category = item.Category, SizeValue = item.Size }));
        db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "Sized", Category = "Clothing", SizeSystem = "US letter", SizeValue = "M" });
        await db.SaveChangesAsync();

        Dictionary<string, InventoryItem> items = await MigrateAsync(db, migrator);

        Assert.Equal(("US letter", "L"), (items["Dress"].SizeSystem, items["Dress"].SizeValue));
        Assert.Equal(("US letter", "2XL"), (items["Tee"].SizeSystem, items["Tee"].SizeValue));
        Assert.Equal(("One size", "One size"), (items["Hat"].SizeSystem, items["Hat"].SizeValue));
        Assert.Equal(("US numeric", "14"), (items["Jeans"].SizeSystem, items["Jeans"].SizeValue));
        Assert.Equal((null, "7"), (items["Heels"].SizeSystem, items["Heels"].SizeValue));
        Assert.Equal((null, "W36"), (items["Belt"].SizeSystem, items["Belt"].SizeValue));
        Assert.Equal(("US letter", "M"), (items["Sized"].SizeSystem, items["Sized"].SizeValue));
    }
}
