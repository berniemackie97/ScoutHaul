using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

/// <summary>
/// Keeps every item's store listing agreeing with the item, from inside SaveChanges — the only doorway all writes
/// pass through. Inventory status is written by many hands (imports, sale workflows, the status endpoint, the mobile
/// app), and a per-call-site sync is the exact forgotten-caller disease that made seven places derive net profit;
/// one choke point cannot be forgotten. The DECISION lives in <see cref="StorePublishing.DesiredListingStatus"/> in
/// the domain; this class is only the mechanism that notices changes and applies the rule in the same transaction.
/// </summary>
public static class StoreListingSynchronizer
{
    /// <summary>Item ids whose shelf state may have changed in this save: items whose status/archive flag moved,
    /// items created directly into a state that matters, and items whose photo set changed (photos gate Active).</summary>
    public static IReadOnlyCollection<Guid> CollectAffectedItemIds(ChangeTracker tracker)
    {
        HashSet<Guid> ids = [];

        foreach (EntityEntry<InventoryItem> entry in tracker.Entries<InventoryItem>())
        {
            bool relevant = entry.State switch
            {
                EntityState.Added => true,
                EntityState.Modified =>
                    entry.Property(item => item.Status).IsModified
                    || entry.Property(item => item.IsArchived).IsModified
                    || entry.Property(item => item.Title).IsModified
                    || entry.Property(item => item.EstimatedSalePrice).IsModified
                    || entry.Property(item => item.SellerAskingPrice).IsModified,
                _ => false,
            };
            if (relevant)
            {
                ids.Add(entry.Entity.Id);
            }
        }

        foreach (EntityEntry<InventoryPhoto> entry in tracker.Entries<InventoryPhoto>())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted)
            {
                ids.Add(entry.Entity.InventoryItemId);
            }
        }

        return ids;
    }

    public static void Apply(HaulScoutDbContext db, IReadOnlyCollection<Guid> itemIds, DateTimeOffset now)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        List<InventoryItem> items = LoadItems(db, itemIds, tracked: true);
        Dictionary<Guid, StoreListing> listings = LoadListings(db, itemIds);
        Reconcile(db, items, listings, now);
    }

    public static async Task ApplyAsync(HaulScoutDbContext db, IReadOnlyCollection<Guid> itemIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        List<InventoryItem> items = await ItemsQuery(db, itemIds).ToListAsync(cancellationToken);
        MergeTracked(db, itemIds, items);
        Dictionary<Guid, StoreListing> listings = await ListingsQuery(db, itemIds)
            .ToDictionaryAsync(listing => listing.InventoryItemId, cancellationToken);
        MergePendingListings(db, listings);
        Reconcile(db, items, listings, now);
    }

    /// <summary>
    /// Applies <see cref="StorePublishing.DesiredListingStatus"/> to each item. Presentation fields are only ever
    /// SEEDED at creation — after that the listing's title, price and copy belong to the operator, and the sync
    /// touches nothing but the lifecycle status.
    /// </summary>
    private static void Reconcile(HaulScoutDbContext db, List<InventoryItem> items, Dictionary<Guid, StoreListing> listings, DateTimeOffset now)
    {
        foreach (InventoryItem item in items)
        {
            StoreListing? listing = listings.GetValueOrDefault(item.Id);
            StoreListingStatus? desired = StorePublishing.DesiredListingStatus(item, listing?.Status);

            if (listing is null)
            {
                if (desired is StoreListingStatus.Active or StoreListingStatus.Draft)
                {
                    db.StoreListings.Add(NewListing(item, desired.Value, now));
                }
                continue;
            }

            if (desired is StoreListingStatus status && listing.Status != status)
            {
                listing.Status = status;
                listing.UpdatedAt = now;
                if (status == StoreListingStatus.Active && listing.PublishedAt is null)
                {
                    listing.PublishedAt = now;
                }
            }
        }
    }

    private static StoreListing NewListing(InventoryItem item, StoreListingStatus status, DateTimeOffset now)
    {
        Guid id = Guid.NewGuid();
        return new StoreListing
        {
            Id = id,
            OrganizationId = item.OrganizationId,
            InventoryItemId = item.Id,
            // The id suffix makes the slug unique by construction — no lookup loop, no race with a concurrent save.
            Slug = $"{Slugify(item.Title)}-{id:N}",
            Title = item.Title,
            Description = item.Description,
            ListPrice = StorePublishing.ShelfPrice(item),
            Status = status,
            PublishedAt = status == StoreListingStatus.Active ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static List<InventoryItem> LoadItems(HaulScoutDbContext db, IReadOnlyCollection<Guid> itemIds, bool tracked)
    {
        List<InventoryItem> items = ItemsQuery(db, itemIds).ToList();
        MergeTracked(db, itemIds, items);
        return items;
    }

    private static IQueryable<InventoryItem> ItemsQuery(HaulScoutDbContext db, IReadOnlyCollection<Guid> itemIds) =>
        db.InventoryItems
            .IgnoreQueryFilters()   // Runs in webhook and import contexts where no tenant is resolved.
            .Include(item => item.Photos)
            .Where(item => itemIds.Contains(item.Id));

    /// <summary>Items being CREATED in this save aren't in the database yet — pick them up from the tracker.</summary>
    private static void MergeTracked(HaulScoutDbContext db, IReadOnlyCollection<Guid> itemIds, List<InventoryItem> items)
    {
        HashSet<Guid> loaded = items.Select(item => item.Id).ToHashSet();
        foreach (EntityEntry<InventoryItem> entry in db.ChangeTracker.Entries<InventoryItem>())
        {
            if (itemIds.Contains(entry.Entity.Id) && !loaded.Contains(entry.Entity.Id) && entry.State != EntityState.Deleted)
            {
                items.Add(entry.Entity);
            }
        }
    }

    private static Dictionary<Guid, StoreListing> LoadListings(HaulScoutDbContext db, IReadOnlyCollection<Guid> itemIds)
    {
        Dictionary<Guid, StoreListing> listings = ListingsQuery(db, itemIds).ToDictionary(listing => listing.InventoryItemId);
        MergePendingListings(db, listings);
        return listings;
    }

    private static IQueryable<StoreListing> ListingsQuery(HaulScoutDbContext db, IReadOnlyCollection<Guid> itemIds) =>
        db.StoreListings
            .IgnoreQueryFilters()
            .Where(listing => itemIds.Contains(listing.InventoryItemId));

    /// <summary>A listing added earlier in this same save (e.g. seeded in a test, or a previous sync pass) is not in
    /// the database yet — without this, the sync would create a second listing for the same item.</summary>
    private static void MergePendingListings(HaulScoutDbContext db, Dictionary<Guid, StoreListing> listings)
    {
        foreach (EntityEntry<StoreListing> entry in db.ChangeTracker.Entries<StoreListing>())
        {
            if (entry.State != EntityState.Deleted)
            {
                listings.TryAdd(entry.Entity.InventoryItemId, entry.Entity);
            }
        }
    }

    private static string Slugify(string value)
    {
        System.Text.StringBuilder builder = new(value.Length);
        bool lastWasHyphen = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "listing" : slug;
    }
}
