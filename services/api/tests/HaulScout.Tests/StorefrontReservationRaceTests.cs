using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Storefront;

namespace HaulScout.Tests;

/// <summary>
/// The one-of-one contention guard against real Postgres — the fakes can only promise all-or-nothing; whether the
/// guarded UPDATE actually serializes two simultaneous checkouts is the database's behaviour, so it is proven here.
/// </summary>
[Collection(PostgresCollection.Name)]
public class StorefrontReservationRaceTests(PostgresFixture postgres)
{
    private static async Task<StoreListing> SeedActiveListing(HaulScoutDbContext db, Guid org, string title = "Brass Lamp")
    {
        InventoryItem item = new()
        {
            OrganizationId = org,
            Title = title,
            PurchasePrice = 40m,
            EstimatedSalePrice = 100m,
            Status = InventoryStatus.Listed,
            // Shoppable for real: the synchronizer would demote an imageless item's listing to Draft.
            Photos = { new InventoryPhoto { OrganizationId = org, ContentType = "image/jpeg", OriginalBytes = [1], ThumbnailBytes = [1] } },
        };
        db.InventoryItems.Add(item);
        StoreListing listing = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            Slug = $"{title.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}"[..24],
            Title = title,
            ListPrice = 100m,
            Status = StoreListingStatus.Active,
        };
        db.StoreListings.Add(listing);
        await db.SaveChangesAsync();
        return listing;
    }

    /// <summary>Two shoppers hit Buy at the same instant. Exactly one may hold the item — never both, never neither.</summary>
    [Fact]
    public async Task Two_simultaneous_checkouts_of_one_item_produce_exactly_one_winner()
    {
        (HaulScoutDbContext seed, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _seed = seed;
        StoreListing listing = await SeedActiveListing(seed, org);

        // Two independent connections, like two real requests.
        await using HaulScoutDbContext dbA = postgres.NewContext(org);
        await using HaulScoutDbContext dbB = postgres.NewContext(org);
        EfStorefrontCheckoutStore storeA = new(dbA);
        EfStorefrontCheckoutStore storeB = new(dbB);

        Task<IReadOnlyList<Guid>> attemptA = storeA.TryReserveAsync(org, [listing.Id], CancellationToken.None);
        Task<IReadOnlyList<Guid>> attemptB = storeB.TryReserveAsync(org, [listing.Id], CancellationToken.None);
        IReadOnlyList<Guid>[] results = await Task.WhenAll(attemptA, attemptB);

        int winners = results.Count(lost => lost.Count == 0);
        Assert.Equal(1, winners);

        await using HaulScoutDbContext check = postgres.NewContext(org);
        StoreListing after = await check.StoreListings.FindAsync(listing.Id) ?? throw new InvalidOperationException();
        Assert.Equal(StoreListingStatus.Reserved, after.Status);
    }

    /// <summary>An item sold on another channel cannot be reserved even while its listing still says Active.</summary>
    [Fact]
    public async Task A_listing_whose_item_left_the_shelf_cannot_be_reserved()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _db = db;
        StoreListing listing = await SeedActiveListing(db, org, "Walnut Desk");
        InventoryItem item = await db.InventoryItems.FindAsync(listing.InventoryItemId) ?? throw new InvalidOperationException();
        item.Status = InventoryStatus.Sold;   // sold on eBay; nothing updated the store listing
        await db.SaveChangesAsync();

        IReadOnlyList<Guid> lost = await new EfStorefrontCheckoutStore(db).TryReserveAsync(org, [listing.Id], CancellationToken.None);

        Assert.Equal([listing.Id], lost);
    }

    /// <summary>Selling requires the reservation: a listing that slipped back to Active is reported, not sold.</summary>
    [Fact]
    public async Task Marking_sold_refuses_listings_that_are_not_reserved()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _db = db;
        StoreListing listing = await SeedActiveListing(db, org, "Copper Kettle");
        EfStorefrontCheckoutStore store = new(db);

        IReadOnlyList<Guid> conflicted = await store.MarkSoldAsync(org, [listing.Id], CancellationToken.None);
        Assert.Equal([listing.Id], conflicted);   // never Reserved — refused

        Assert.Empty(await store.TryReserveAsync(org, [listing.Id], CancellationToken.None));
        Assert.Empty(await store.MarkSoldAsync(org, [listing.Id], CancellationToken.None));   // Reserved → Sold
    }

    /// <summary>Release is the expiry path: Reserved returns to Active, anything else is left alone.</summary>
    [Fact]
    public async Task Releasing_returns_reserved_listings_to_the_shelf_and_touches_nothing_else()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _db = db;
        StoreListing reserved = await SeedActiveListing(db, org, "Oak Chair");
        StoreListing sold = await SeedActiveListing(db, org, "Tin Sign");
        EfStorefrontCheckoutStore store = new(db);
        await store.TryReserveAsync(org, [reserved.Id, sold.Id], CancellationToken.None);
        await store.MarkSoldAsync(org, [sold.Id], CancellationToken.None);

        await store.ReleaseAsync(org, [reserved.Id, sold.Id], CancellationToken.None);

        await using HaulScoutDbContext check = postgres.NewContext(org);
        Assert.Equal(StoreListingStatus.Active, (await check.StoreListings.FindAsync(reserved.Id))!.Status);
        Assert.Equal(StoreListingStatus.Sold, (await check.StoreListings.FindAsync(sold.Id))!.Status);   // sold stays sold
    }
}
