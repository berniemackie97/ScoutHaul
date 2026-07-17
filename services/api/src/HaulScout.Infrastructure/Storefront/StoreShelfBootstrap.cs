using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HaulScout.Domain;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

/// <summary>
/// Runs the shelf rule over the whole catalog at startup, right after migrations. The save-time synchronizer only
/// sees CHANGES, so rows that predate it — or anything written around EF — would otherwise stay off the shelf until
/// someone remembered the reconcile endpoint. Nobody remembers; deploys should not depend on memory. Idempotent and
/// cheap at this scale, so every boot self-heals instead of only the first one.
/// </summary>
public static class StoreShelfBootstrap
{
    public static async Task ReconcileAllAsync(HaulScoutDbContext db, TimeProvider clock, ILogger logger, CancellationToken cancellationToken = default)
    {
        List<Guid> itemIds = await db.InventoryItems
            .IgnoreQueryFilters()   // every organization's shelf, not a request tenant's
            .Where(item => item.Status == InventoryStatus.Listed
                || db.StoreListings.IgnoreQueryFilters().Any(listing => listing.InventoryItemId == item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        await StoreListingSynchronizer.ApplyAsync(db, itemIds, clock.GetUtcNow(), cancellationToken);
        int written = await db.SaveChangesAsync(cancellationToken);

        int active = await db.StoreListings.IgnoreQueryFilters().CountAsync(listing => listing.Status == StoreListingStatus.Active, cancellationToken);
        int awaitingPhotos = await db.StoreListings.IgnoreQueryFilters().CountAsync(
            listing => listing.Status == StoreListingStatus.Draft
                && listing.InventoryItem!.Status == InventoryStatus.Listed,
            cancellationToken);

        logger.LogInformation(
            "Shelf reconcile: examined {Examined} items, wrote {Written} changes — {Active} live on the storefront, {AwaitingPhotos} listed but awaiting photos.",
            itemIds.Count, written, active, awaitingPhotos);
    }
}
