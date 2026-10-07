using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Common;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Merging duplicate items on real Postgres, where the foreign keys and the one-per-item unique indexes are enforced.
/// A merge that missed a reference would cascade it away or fail the save; in memory, neither would show.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InventoryMergeStoreTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static InventoryMergeService NewService(HaulScoutDbContext db, Guid org, Guid user)
    {
        StubCurrentUser currentUser = new(org, user);
        return new InventoryMergeService(new EfInventoryMergeStore(db), new DatabaseInventoryPhotoBlobStore(), new EfAuditTrail(db, currentUser), currentUser, new EfUnitOfWork(db));
    }

    private static InventoryItem Item(Guid org, string title, InventoryStatus status) =>
        new() { OrganizationId = org, Title = title, Status = status, PurchasePrice = 20m, EstimatedSalePrice = 80m };

    private static InventoryPhoto Photo(Guid org, string? source, bool hero = false) =>
        new() { OrganizationId = org, SourceUrl = source, IsHero = hero, ContentType = "image/jpeg", OriginalBytes = [1], ThumbnailBytes = [1] };

    private static ChannelAccount Account(Guid org, string channel) => new() { OrganizationId = org, Channel = channel, DisplayName = channel };

    private static ChannelListing Listing(Guid org, InventoryItem item, ChannelAccount account, ChannelListingStatus status) =>
        new() { OrganizationId = org, InventoryItemId = item.Id, ChannelAccountId = account.Id, Status = status };

    private static Sale SaleOf(Guid org, InventoryItem item, SaleStatus status) => new()
    {
        OrganizationId = org,
        InventoryItemId = item.Id,
        Platform = "eBay",
        SoldPrice = 80m,
        CostOfGoods = 0m,
        Status = status,
        SoldAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task The_duplicates_sale_photos_and_listings_move_to_the_kept_item_and_the_duplicate_is_gone()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext scope = db;
        ChannelAccount ebay = Account(org, "eBay");
        ChannelAccount depop = Account(org, "Depop");
        InventoryItem kept = Item(org, "Nintendo 64 console", InventoryStatus.Listed);
        kept.Photos.Add(Photo(org, "https://img.example/front.jpg", hero: true));
        InventoryItem duplicate = Item(org, "N64 console grey", InventoryStatus.Sold);
        duplicate.Brand = "Nintendo";
        InventoryPhoto samePicture = Photo(org, "https://img.example/front.jpg", hero: true);
        duplicate.Photos.Add(samePicture);
        duplicate.Photos.Add(Photo(org, "https://img.example/back.jpg"));
        Order order = new() { OrganizationId = org, Channel = "eBay", OrderNumber = $"EB-{Guid.NewGuid():N}"[..12], Status = OrderStatus.Paid, PaymentStatus = PaymentStatus.Paid };
        Sale sale = SaleOf(org, duplicate, SaleStatus.Delivered);
        db.AddRange(ebay, depop, kept, duplicate, order, sale);
        db.OrderLines.Add(new OrderLine { OrganizationId = org, OrderId = order.Id, InventoryItemId = duplicate.Id, TitleSnapshot = duplicate.Title, Quantity = 1, UnitPrice = 80m, LineTotal = 80m });
        db.ChannelListings.AddRange(
            Listing(org, kept, ebay, ChannelListingStatus.Active),
            Listing(org, duplicate, ebay, ChannelListingStatus.Sold),
            Listing(org, duplicate, depop, ChannelListingStatus.Active));
        db.Expenses.Add(new Expense { OrganizationId = org, InventoryItemId = duplicate.Id, Description = "Shipping label", Amount = 9m });
        InventoryIssue scratch = new() { OrganizationId = org, InventoryItemId = duplicate.Id, InventoryPhotoId = samePicture.Id, IssueType = "Cosmetic", Severity = "Minor" };
        db.InventoryIssues.Add(scratch);
        await db.SaveChangesAsync();

        AppResult<MergeInventoryItemsResultDto> result = await NewService(db, org, user).MergeAsync(new MergeInventoryItemsRequest(kept.Id, [duplicate.Id]), CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        db.ChangeTracker.Clear();
        Assert.False(await db.InventoryItems.AnyAsync(item => item.Id == duplicate.Id));
        InventoryItem merged = await db.InventoryItems.Include(item => item.Photos).SingleAsync(item => item.Id == kept.Id);
        Assert.Equal(InventoryStatus.Sold, merged.Status);
        Assert.Equal("Nintendo", merged.Brand);
        Assert.Equal(["https://img.example/front.jpg", "https://img.example/back.jpg"], InventoryPhotoSet.Ordered(merged).Select(photo => photo.SourceUrl));
        Assert.True(InventoryPhotoSet.Ordered(merged)[0].IsHero);
        Assert.Equal(kept.Id, (await db.Sales.SingleAsync(entry => entry.Id == sale.Id)).InventoryItemId);
        Assert.Equal(kept.Id, (await db.OrderLines.SingleAsync(line => line.OrderId == order.Id)).InventoryItemId);
        Assert.Equal(kept.Id, (await db.Expenses.SingleAsync(expense => expense.Description == "Shipping label")).InventoryItemId);
        Assert.Equal(merged.Photos.Single(photo => photo.SourceUrl == "https://img.example/front.jpg").Id, (await db.InventoryIssues.SingleAsync(issue => issue.Id == scratch.Id)).InventoryPhotoId);

        List<ChannelListing> listings = await db.ChannelListings.Where(listing => listing.InventoryItemId == kept.Id).ToListAsync();
        Assert.Equal(ChannelListingStatus.Sold, listings.Single(listing => listing.ChannelAccountId == ebay.Id).Status);
        Assert.Equal(ChannelListingStatus.Active, listings.Single(listing => listing.ChannelAccountId == depop.Id).Status);
        Assert.True(await db.AuditLogs.AnyAsync(entry => entry.EntityId == kept.Id && entry.Action == "InventoryItemsMerged"));
    }

    [Fact]
    public async Task Several_duplicates_listed_on_one_account_leave_the_kept_item_one_listing_there()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext scope = db;
        ChannelAccount vinted = Account(org, "Vinted");
        InventoryItem kept = Item(org, "Wool coat", InventoryStatus.ReadyToList);
        InventoryItem first = Item(org, "Wool coat (Vinted)", InventoryStatus.Listed);
        InventoryItem second = Item(org, "Wool coat copy", InventoryStatus.Draft);
        db.AddRange(vinted, kept, first, second);
        db.ChannelListings.AddRange(Listing(org, first, vinted, ChannelListingStatus.Active), Listing(org, second, vinted, ChannelListingStatus.Delisted));
        await db.SaveChangesAsync();

        AppResult<MergeInventoryItemsResultDto> result = await NewService(db, org, user).MergeAsync(new MergeInventoryItemsRequest(kept.Id, [first.Id, second.Id]), CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        Assert.Equal(2, result.Value!.Merged);
        db.ChangeTracker.Clear();
        ChannelListing listing = await db.ChannelListings.SingleAsync(entry => entry.ChannelAccountId == vinted.Id);
        Assert.Equal((kept.Id, ChannelListingStatus.Active), (listing.InventoryItemId, listing.Status));
        Assert.Equal(1, await db.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task A_merge_keeps_details_a_marketplace_category_would_not_have_told_it_about()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext scope = db;
        InventoryItem kept = Item(org, "Floral blouse", InventoryStatus.Listed);
        kept.Category = "Tops";
        kept.SizeSystem = "US letter";
        kept.SizeValue = "M";
        InventoryItem duplicate = Item(org, "Floral blouse M", InventoryStatus.Sold);
        duplicate.Colors = ["Pink", "White"];
        db.AddRange(kept, duplicate);
        await db.SaveChangesAsync();

        AppResult<MergeInventoryItemsResultDto> result = await NewService(db, org, user).MergeAsync(new MergeInventoryItemsRequest(kept.Id, [duplicate.Id]), CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        db.ChangeTracker.Clear();
        InventoryItem merged = await db.InventoryItems.SingleAsync(item => item.Id == kept.Id);
        Assert.Equal(("US letter", "M"), (merged.SizeSystem, merged.SizeValue));
        Assert.Equal(["Pink", "White"], merged.Colors);
    }

    [Fact]
    public async Task Two_items_each_with_a_live_sale_are_refused_and_left_as_they_were()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext scope = db;
        InventoryItem kept = Item(org, "Brass lamp", InventoryStatus.Sold);
        InventoryItem other = Item(org, "Brass lamp", InventoryStatus.Sold);
        db.AddRange(kept, other, SaleOf(org, kept, SaleStatus.Shipped), SaleOf(org, other, SaleStatus.Recorded));
        await db.SaveChangesAsync();

        AppResult<MergeInventoryItemsResultDto> result = await NewService(db, org, user).MergeAsync(new MergeInventoryItemsRequest(kept.Id, [other.Id]), CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.InventoryItems.CountAsync());
        Assert.Equal(1, await db.Sales.CountAsync(sale => sale.InventoryItemId == other.Id));
    }

    [Fact]
    public async Task An_item_cannot_be_merged_into_itself_or_with_an_item_from_another_organization()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext scope = db;
        (HaulScoutDbContext otherDb, Guid otherOrg, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext otherScope = otherDb;
        InventoryItem kept = Item(org, "Lamp", InventoryStatus.Draft);
        InventoryItem foreign = Item(otherOrg, "Lamp", InventoryStatus.Draft);
        db.Add(kept);
        otherDb.Add(foreign);
        await db.SaveChangesAsync();
        await otherDb.SaveChangesAsync();
        InventoryMergeService merges = NewService(db, org, user);

        Assert.Equal(AppResultStatus.Validation, (await merges.MergeAsync(new MergeInventoryItemsRequest(kept.Id, [kept.Id]), CancellationToken.None)).Status);
        Assert.Equal(AppResultStatus.NotFound, (await merges.MergeAsync(new MergeInventoryItemsRequest(kept.Id, [foreign.Id]), CancellationToken.None)).Status);
        Assert.True(await otherDb.InventoryItems.AnyAsync(item => item.Id == foreign.Id));
    }

    /// <summary>
    /// A merge deletes the duplicates, and a sale's row cascades with its item. While the merge runs, a sale written
    /// to one of its records must wait (and then fail on the deleted item), never be deleted along with it.
    /// </summary>
    [Fact]
    public async Task A_record_being_merged_holds_off_a_sale_written_to_it_meanwhile()
    {
        (HaulScoutDbContext db, Guid org, _) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext scope = db;
        InventoryItem item = Item(org, "Brass lamp", InventoryStatus.Listed);
        db.Add(item);
        await db.SaveChangesAsync();
        await using IDbContextTransaction merging = await db.Database.BeginTransactionAsync();
        await new EfInventoryMergeStore(db).LockItemsAsync(org, [item.Id], CancellationToken.None);

        await using HaulScoutDbContext importer = postgres.NewContext(org);
        await importer.Database.OpenConnectionAsync();
        await importer.Database.ExecuteSqlRawAsync("SET lock_timeout = '200ms'");
        importer.Sales.Add(SaleOf(org, item, SaleStatus.Recorded));

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => importer.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(failure.GetBaseException()).SqlState);
    }

    /// <summary>
    /// The merge store names every column that refers to an item. When this fails, a new reference was added: move it
    /// in <see cref="EfInventoryMergeStore.MoveReferencesAsync"/> (or refuse it, as bundles are), then list it here.
    /// </summary>
    [Fact]
    public void Every_column_that_refers_to_an_inventory_item_is_one_the_merge_handles()
    {
        using HaulScoutDbContext db = postgres.NewContext(Guid.NewGuid());
        string[] handled =
        [
            "AcquisitionLotItem.InventoryItemId", "BuyRecommendation.InventoryItemId", "CartLine.InventoryItemId",
            "ChannelListing.InventoryItemId", "Expense.InventoryItemId", "InventoryBundleComponent.BundleInventoryItemId",
            "InventoryBundleComponent.ComponentInventoryItemId", "InventoryIssue.InventoryItemId", "InventoryPhoto.InventoryItemId",
            "Listing.InventoryItemId", "MarketAnalysis.InventoryItemId", "MarketObservation.InventoryItemId", "OrderLine.InventoryItemId",
            "PlatformListing.ReconciledInventoryItemId", "PlatformOrderLine.InventoryItemId", "ResaleTask.InventoryItemId",
            "Sale.InventoryItemId", "StoreListing.InventoryItemId", "StorefrontOffer.InventoryItemId",
        ];

        IEnumerable<string> referring = db.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties().Select(property => (type, property)))
            .Where(pair => pair.property.Name.EndsWith("InventoryItemId", StringComparison.Ordinal)
                || pair.property.GetContainingForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(InventoryItem)))
            .Select(pair => $"{pair.type.ClrType.Name}.{pair.property.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(handled.Order(StringComparer.Ordinal), referring);
    }
}
