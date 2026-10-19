using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Appraisal;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Appraisal;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>The appraisal comp queries against Postgres, where the in-memory provider cannot vouch for them.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AppraisalTranslationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Own_sales_saved_comps_and_the_catalog_translate_to_sql()
    {
        (HaulScoutDbContext db, Guid org, Guid _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        InventoryItem item = new() { OrganizationId = org, Title = "Sony WH-1000XM5 Headphones", Brand = "Sony", Barcode = "027242923534", Status = InventoryStatus.Sold };
        ProductIdentity product = new() { OrganizationId = org, Title = "Sony WH-1000XM5", Upc = "027242923541" };
        db.InventoryItems.Add(item);
        db.ProductIdentities.Add(product);
        db.MarketObservations.Add(new MarketObservation
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            ObservationType = MarketObservationType.InternalSale,
            Source = "eBay",
            Title = item.Title,
            Price = 210m,
            TotalPrice = 220m,
            ObservedAt = now.AddDays(-9),
            SoldAt = now.AddDays(-9),
        });
        db.MarketObservations.Add(new MarketObservation
        {
            OrganizationId = org,
            ProductIdentityId = product.Id,
            ObservationType = MarketObservationType.ManualSold,
            Source = "eBay",
            Title = "Sony WH-1000XM5 Black",
            Price = 200m,
            TotalPrice = 200m,
            ObservedAt = now.AddDays(-3),
            SoldAt = now.AddDays(-3),
        });
        await db.SaveChangesAsync();

        CompQuery query = AppraisalCompSourceTests.Query(org, "Sony WH-1000XM5 Headphones", "Sony", "WH-1000XM5");
        CompSourceResult own = await new OwnSalesCompSource(db).FindAsync(query, CancellationToken.None);
        CompSourceResult saved = await new SavedCompSource(db, new FakeTimeProvider(now))
            .FindAsync(query with { ProductIdentityId = product.Id }, CancellationToken.None);
        EfProductCatalogStore catalog = new(db);

        Assert.Equal(210m, Assert.Single(own.Comps).Price);
        Assert.Equal(200m, Assert.Single(saved.Comps).Price);
        Assert.Equal(product.Id, (await catalog.FindByBarcodeAsync(org, "027242923541", CancellationToken.None))?.ProductIdentityId);
        Assert.Equal(item.Title, (await catalog.FindByBarcodeAsync(org, "027242923534", CancellationToken.None))?.Title);
    }
}
