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

    [Fact]
    public async Task Appraisal_history_round_trips_through_sql()
    {
        (HaulScoutDbContext db, Guid org, Guid _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        InventoryItem item = new() { OrganizationId = org, Title = "Brass lamp", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        EfAppraisalHistoryStore store = new(db);
        Comp sale = new(CompKind.Sold, "eBay", "Brass lamp", 50m, null, null, null, DateTimeOffset.UtcNow, true);

        await store.RecordAsync(org, item.Id, null,
            new PriceResult(PriceBasis.Sales, 40m, 49.99m, 55m, 62, ConfidenceLevel.Fair, ["1 matching sale."], [new AssessedComp(sale, true, null)]),
            CancellationToken.None);

        AppraisalSnapshotDto snapshot = Assert.Single(await store.ListForItemAsync(org, item.Id, 20, CancellationToken.None));
        Assert.Equal(49.99m, snapshot.List);
        Assert.Equal(PriceBasis.Sales, snapshot.Basis);
        Assert.True(await store.ItemExistsAsync(org, item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Channel_sales_history_translates_to_sql()
    {
        (HaulScoutDbContext db, Guid org, Guid _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        db.ChannelAccounts.Add(new ChannelAccount { OrganizationId = org, Channel = "eBay", DisplayName = "shop (ebay)", DefaultFeeProfileId = "store-subscriber" });
        db.ChannelAccounts.Add(new ChannelAccount { OrganizationId = org, Channel = "Depop", DisplayName = "shop (depop)", IsActive = false });
        foreach ((decimal price, SaleStatus status) in new[] { (50m, SaleStatus.Delivered), (30m, SaleStatus.Recorded), (70m, SaleStatus.Refunded) })
        {
            InventoryItem item = new() { OrganizationId = org, Title = $"Item {price}", Status = InventoryStatus.Sold };
            db.InventoryItems.Add(item);
            db.Sales.Add(new Sale
            {
                OrganizationId = org,
                InventoryItemId = item.Id,
                Platform = "eBay",
                SoldPrice = price,
                PlatformFees = price * 0.1m,
                ShippingCost = 5m,
                ShippingCharged = 6m,
                Status = status,
                SoldAt = now.AddDays(-10),
            });
        }

        await db.SaveChangesAsync();

        EfChannelEconomicsStore store = new(db);
        IReadOnlyList<ChannelAccountRef> channels = await store.ListActiveChannelsAsync(org, CancellationToken.None);
        IReadOnlyDictionary<string, ChannelSalesHistory> history = await store.SalesHistoryAsync(org, now.AddDays(-365), CancellationToken.None);

        Assert.Equal("eBay", Assert.Single(channels).Channel);
        ChannelSalesHistory ebay = history["EBAY"];
        Assert.Equal(2, ebay.Sales);
        Assert.Equal(80m, ebay.SoldTotal);
        Assert.Equal(8m, ebay.FeeTotal);
        Assert.Equal(-2m, ebay.NetShippingTotal);
    }
}
