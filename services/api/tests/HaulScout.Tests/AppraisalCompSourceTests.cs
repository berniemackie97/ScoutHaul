using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Common;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Appraisal;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public sealed class AppraisalCompSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Your_sales_of_the_same_item_are_found_by_name_on_every_channel()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Your_sales_of_the_same_item_are_found_by_name_on_every_channel), org);
        AddSale(db, org, "Sony WH-1000XM5 Wireless Headphones Black", 210m, "eBay", daysAgo: 12);
        AddSale(db, org, "Sony WH1000XM5 WH-1000XM5 Noise Cancelling", 195m, "Poshmark", daysAgo: 40);
        AddSale(db, org, "Sony WH-1000XM4 Headphones", 150m, "eBay", daysAgo: 5);
        AddSale(db, org, "Bose QC45 Headphones", 180m, "Internal", daysAgo: 3);
        AddSale(db, org, "Sony WH-1000XM5 Headphones", 205m, "eBay", daysAgo: 1, archived: true);
        AddSale(db, Guid.NewGuid(), "Sony WH-1000XM5 Headphones", 999m, "eBay", daysAgo: 1);
        await db.SaveChangesAsync();

        CompSourceResult result = await new OwnSalesCompSource(db).FindAsync(Query(org, "Sony WH-1000XM5 Headphones", "Sony", "WH-1000XM5"), CancellationToken.None);

        Assert.Equal([210m, 195m], result.Comps.Select(comp => comp.Price).ToArray());
        Assert.All(result.Comps, comp => Assert.Equal(CompKind.YourSale, comp.Kind));
        Assert.Contains(result.Comps, comp => comp.Source == "Your Poshmark sale");
    }

    [Fact]
    public async Task A_recorded_sale_reads_as_your_sale()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(A_recorded_sale_reads_as_your_sale), org);
        AddSale(db, org, "Le Creuset Dutch Oven 5.5 qt Flame", 140m, "Internal", daysAgo: 30);
        await db.SaveChangesAsync();

        CompSourceResult result = await new OwnSalesCompSource(db).FindAsync(Query(org, "Le Creuset Dutch Oven 5.5 qt", "Le Creuset", null), CancellationToken.None);

        Assert.Equal("Your sale", Assert.Single(result.Comps).Source);
    }

    [Fact]
    public async Task Saved_comps_skip_anything_a_model_wrote_and_anything_over_a_year_old()
    {
        Guid org = Guid.NewGuid();
        Guid product = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Saved_comps_skip_anything_a_model_wrote_and_anything_over_a_year_old), org);
        db.ProductIdentities.Add(new ProductIdentity { Id = product, OrganizationId = org, Title = "Sony WH-1000XM5" });
        AddObservation(db, org, product, MarketObservationType.ManualSold, "eBay", 200m, daysAgo: 20);
        AddObservation(db, org, product, MarketObservationType.ManualActive, "Facebook", 260m, daysAgo: 2);
        AddObservation(db, org, product, MarketObservationType.Sold, "AI Comp (web)", 400m, daysAgo: 2);
        AddObservation(db, org, product, MarketObservationType.Estimate, "AI Estimate", 300m, daysAgo: 2);
        AddObservation(db, org, product, MarketObservationType.Sold, "eBay", 90m, daysAgo: 400);
        await db.SaveChangesAsync();

        CompSourceResult result = await new SavedCompSource(db, new FakeTimeProvider(Now))
            .FindAsync(Query(org, "Sony WH-1000XM5", "Sony", null) with { ProductIdentityId = product }, CancellationToken.None);

        Assert.Equal([CompKind.Asking, CompKind.Sold], result.Comps.Select(comp => comp.Kind).ToArray());
        Assert.DoesNotContain(result.Comps, comp => comp.Source.StartsWith("AI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_barcode_on_an_item_the_business_stocked_identifies_it()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(A_barcode_on_an_item_the_business_stocked_identifies_it), org);
        db.InventoryItems.Add(new InventoryItem { OrganizationId = org, Title = "Instant Pot Duo 6qt", Brand = "Instant Pot", Barcode = "853084004088", Status = InventoryStatus.Sold });
        await db.SaveChangesAsync();

        CatalogProduct? found = await new EfProductCatalogStore(db).FindByBarcodeAsync(org, "853084004088", CancellationToken.None);

        Assert.Equal("Instant Pot Duo 6qt", found?.Title);
        Assert.Null(found?.ProductIdentityId);
    }

    internal static CompQuery Query(Guid org, string title, string? brand, string? model) =>
        new(org, new ItemFacts(title, brand, model, null, null, null, [], IdentificationCertainty.High), title, null, null);

    internal static void AddSale(HaulScoutDbContext db, Guid org, string title, decimal price, string source, int daysAgo, bool archived = false)
    {
        db.MarketObservations.Add(new MarketObservation
        {
            OrganizationId = org,
            ObservationType = MarketObservationType.InternalSale,
            Source = source,
            Title = title,
            Price = price,
            TotalPrice = price,
            Condition = "Good",
            ObservedAt = Now.AddDays(-daysAgo),
            SoldAt = Now.AddDays(-daysAgo),
            IsArchived = archived,
        });
    }

    private static void AddObservation(HaulScoutDbContext db, Guid org, Guid product, MarketObservationType type, string source, decimal price, int daysAgo)
    {
        db.MarketObservations.Add(new MarketObservation
        {
            OrganizationId = org,
            ProductIdentityId = product,
            ObservationType = type,
            Source = source,
            Title = "Sony WH-1000XM5 Headphones",
            Price = price,
            TotalPrice = price,
            ObservedAt = Now.AddDays(-daysAgo),
            SoldAt = type is MarketObservationType.Sold or MarketObservationType.ManualSold ? Now.AddDays(-daysAgo) : null,
        });
    }

    private static HaulScoutDbContext NewDb(string store, Guid org)
    {
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return db;
    }
}
