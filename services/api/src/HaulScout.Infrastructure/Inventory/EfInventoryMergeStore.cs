using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// Moves every row that points at a duplicate onto the item kept. The list is every column that refers to an
/// inventory item; a new one belongs here too, or a merge would delete or strand what hangs off it.
/// </summary>
public sealed class EfInventoryMergeStore(HaulScoutDbContext db) : IInventoryMergeStore
{
    public async Task<IReadOnlyList<InventoryItem>> LockItemsAsync(Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        // In id order, so two merges over the same items queue rather than deadlock.
        Guid[] keys = [.. ids];
        await db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "InventoryItems" WHERE "OrganizationId" = {organizationId} AND "Id" = ANY({keys}) ORDER BY "Id" FOR UPDATE""",
            cancellationToken);
        return await db.InventoryItems
            .Include(item => item.Photos)
            .Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<MergeFootprint> InspectAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken)
    {
        StoreListing? listing = await db.StoreListings
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == itemId, cancellationToken);
        bool listingHasHistory = listing is not null && (
            await db.CartLines.AnyAsync(line => line.StoreListingId == listing.Id, cancellationToken)
            || await db.OrderLines.AnyAsync(line => line.StoreListingId == listing.Id, cancellationToken)
            || await db.StorefrontOffers.AnyAsync(offer => offer.StoreListingId == listing.Id, cancellationToken)
            || await db.WishlistItems.AnyAsync(entry => entry.StoreListingId == listing.Id, cancellationToken)
            || await db.Reviews.AnyAsync(review => review.StoreListingId == listing.Id, cancellationToken));

        return new MergeFootprint(
            LiveSales: await db.Sales.CountAsync(
                sale => sale.OrganizationId == organizationId
                    && sale.InventoryItemId == itemId
                    && !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status),
                cancellationToken),
            InPurchaseLot: await db.AcquisitionLotItems.AnyAsync(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == itemId, cancellationToken),
            InBundle: await db.InventoryBundleComponents.AnyAsync(
                component => component.OrganizationId == organizationId
                    && (component.ComponentInventoryItemId == itemId || component.BundleInventoryItemId == itemId),
                cancellationToken),
            StoreListing: listing?.Status,
            StoreListingHasHistory: listingHasHistory);
    }

    public async Task MoveReferencesAsync(InventoryItem from, InventoryItem into, CancellationToken cancellationToken)
    {
        Guid organizationId = from.OrganizationId;
        Guid source = from.Id;
        Guid target = into.Id;

        foreach (Sale entry in await db.Sales.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (OrderLine entry in await db.OrderLines.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (PlatformOrderLine entry in await db.PlatformOrderLines.Where(entry => entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (PlatformListing entry in await db.PlatformListings.Where(entry => entry.OrganizationId == organizationId && entry.ReconciledInventoryItemId == source).ToListAsync(cancellationToken)) entry.ReconciledInventoryItemId = target;
        foreach (AcquisitionLotItem entry in await db.AcquisitionLotItems.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (InventoryIssue entry in await db.InventoryIssues.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (Expense entry in await db.Expenses.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (ResaleTask entry in await db.Tasks.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (MarketObservation entry in await db.MarketObservations.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (MarketAnalysis entry in await db.MarketAnalyses.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (BuyRecommendation entry in await db.BuyRecommendations.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (Listing entry in await db.Listings.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (CartLine entry in await db.CartLines.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;
        foreach (StorefrontOffer entry in await db.StorefrontOffers.Where(entry => entry.OrganizationId == organizationId && entry.InventoryItemId == source).ToListAsync(cancellationToken)) entry.InventoryItemId = target;

        await MoveStoreListingAsync(organizationId, source, target, cancellationToken);
        await MoveChannelListingsAsync(organizationId, source, target, cancellationToken);
    }

    public async Task DropPhotoAsync(InventoryPhoto photo, InventoryPhoto keptCopy, CancellationToken cancellationToken)
    {
        foreach (InventoryIssue issue in await db.InventoryIssues.Where(issue => issue.InventoryPhotoId == photo.Id).ToListAsync(cancellationToken))
        {
            issue.InventoryPhotoId = keptCopy.Id;
        }

        db.InventoryPhotos.Remove(photo);
    }

    public void Remove(InventoryItem item) => db.InventoryItems.Remove(item);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <summary>The kept item's storefront record stays; the duplicate's moves across only when the kept item has none.</summary>
    private async Task MoveStoreListingAsync(Guid organizationId, Guid source, Guid target, CancellationToken cancellationToken)
    {
        // Loaded rather than queried, so a record an earlier duplicate already moved across is seen.
        await db.StoreListings.Where(entry => entry.OrganizationId == organizationId && (entry.InventoryItemId == source || entry.InventoryItemId == target)).LoadAsync(cancellationToken);
        StoreListing? duplicate = db.StoreListings.Local.FirstOrDefault(entry => entry.InventoryItemId == source);
        if (duplicate is null)
        {
            return;
        }

        if (db.StoreListings.Local.Any(entry => entry.InventoryItemId == target))
        {
            db.StoreListings.Remove(duplicate);
        }
        else
        {
            duplicate.InventoryItemId = target;
        }
    }

    private async Task MoveChannelListingsAsync(Guid organizationId, Guid source, Guid target, CancellationToken cancellationToken)
    {
        await db.ChannelListings.Where(entry => entry.OrganizationId == organizationId && (entry.InventoryItemId == source || entry.InventoryItemId == target)).LoadAsync(cancellationToken);
        Dictionary<Guid, ChannelListing> kept = db.ChannelListings.Local.Where(entry => entry.InventoryItemId == target).ToDictionary(entry => entry.ChannelAccountId);
        foreach (ChannelListing duplicate in db.ChannelListings.Local.Where(entry => entry.InventoryItemId == source).ToList())
        {
            if (!kept.TryGetValue(duplicate.ChannelAccountId, out ChannelListing? existing))
            {
                duplicate.InventoryItemId = target;
                continue;
            }

            // One row per item and account: keep whichever says more, and drop the other.
            if (InventoryMerge.Preferred(existing, duplicate) == duplicate)
            {
                db.ChannelListings.Remove(existing);
                duplicate.InventoryItemId = target;
            }
            else
            {
                db.ChannelListings.Remove(duplicate);
            }
        }
    }
}
