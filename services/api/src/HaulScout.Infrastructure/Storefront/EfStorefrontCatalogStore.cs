using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

/// <summary>The SQL behind the public catalog. Anonymous reads, explicit org predicates throughout.</summary>
public sealed class EfStorefrontCatalogStore(HaulScoutDbContext db) : IStorefrontCatalogStore
{
    public async Task<(IReadOnlyList<CatalogRow> Rows, int Total)> BrowseAsync(
        Guid organizationId,
        string? categoryName,
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        IQueryable<StoreListing> query = Shelf(organizationId);

        if (categoryName is not null)
        {
            query = query.Where(listing => listing.InventoryItem!.Category == categoryName);
        }

        if (search is not null)
        {
            string term = $"%{search}%";
            query = query.Where(listing =>
                EF.Functions.ILike(listing.Title, term)
                || (listing.InventoryItem!.Brand != null && EF.Functions.ILike(listing.InventoryItem.Brand, term)));
        }

        int total = await query.CountAsync(cancellationToken);

        List<CatalogRow> rows = await query
            .OrderByDescending(listing => listing.IsFeatured)
            .ThenByDescending(listing => listing.PublishedAt ?? listing.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(listing => new CatalogRow(
                listing.Id,
                listing.Title,
                listing.Subtitle,
                listing.ListPrice,
                listing.CompareAtPrice,
                listing.IsFeatured,
                listing.StoreCategoryId,
                listing.InventoryItem!.Condition,
                listing.InventoryItem.Brand,
                listing.InventoryItem.Category,
                listing.InventoryItem.Photos
                    .OrderByDescending(photo => photo.IsHero)
                    .ThenBy(photo => photo.SortOrder)
                    .Select(photo => (Guid?)photo.Id)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return (rows, total);
    }

    public Task<CatalogDetailRow?> FindDetailAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken) =>
        db.StoreListings
            .Where(listing => listing.Id == listingId
                && listing.OrganizationId == organizationId
                && listing.Status != StoreListingStatus.Draft
                && listing.Status != StoreListingStatus.Archived)
            .Select(listing => new CatalogDetailRow(
                listing.Id,
                listing.Title,
                listing.Subtitle,
                listing.Description ?? listing.InventoryItem!.Description,
                listing.ListPrice,
                listing.CompareAtPrice,
                listing.MetaTitle,
                listing.MetaDescription,
                listing.Status,
                new CatalogItemSpecs(
                    listing.InventoryItem!.Brand,
                    listing.InventoryItem.Model,
                    listing.InventoryItem.Category,
                    listing.InventoryItem.Color,
                    listing.InventoryItem.Condition,
                    listing.InventoryItem.SizeSystem,
                    listing.InventoryItem.SizeValue,
                    listing.InventoryItem.FunctionalStatus,
                    listing.InventoryItem.CompletenessStatus,
                    listing.InventoryItem.PackagingStatus,
                    listing.InventoryItem.CountryOfOrigin,
                    listing.InventoryItem.ManufactureYear,
                    listing.InventoryItem.Length,
                    listing.InventoryItem.Width,
                    listing.InventoryItem.Height,
                    listing.InventoryItem.DimensionUnit,
                    listing.InventoryItem.Weight,
                    listing.InventoryItem.WeightUnit),
                listing.InventoryItem.Photos
                    .OrderByDescending(photo => photo.IsHero)
                    .ThenBy(photo => photo.SortOrder)
                    .Select(photo => photo.Id)
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<(string Name, int Count)>> ListCategoriesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        (await Shelf(organizationId)
            .Where(listing => listing.InventoryItem!.Category != null && listing.InventoryItem.Category != "")
            .GroupBy(listing => listing.InventoryItem!.Category!)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ToListAsync(cancellationToken))
        .Select(group => (group.Name, group.Count))
        .ToList();

    /// <summary>
    /// Public exactly when the item has a publicly visible store listing — the same shelf rule as the catalog, so a
    /// photo can never outlive its listing's visibility. Org-agnostic and filter-free on purpose: browsers fetch
    /// images without the store header, and the photo id is unguessable.
    /// </summary>
    public Task<PublicPhoto?> FindPublicPhotoAsync(Guid photoId, bool fullSize, CancellationToken cancellationToken) =>
        db.InventoryPhotos
            .IgnoreQueryFilters()
            .Where(photo => photo.Id == photoId
                && photo.InventoryItem!.Organization!.StoreDomain != null
                && db.StoreListings.IgnoreQueryFilters().Any(listing =>
                    listing.InventoryItemId == photo.InventoryItemId
                    && listing.Status != StoreListingStatus.Draft
                    && listing.Status != StoreListingStatus.Archived))
            .Select(photo => new PublicPhoto(
                fullSize ? photo.OriginalBytes : photo.ThumbnailBytes,
                fullSize ? photo.ContentType : "image/jpeg"))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>What shoppers browse: Active listings only. Reserved/Sold stay reachable by direct link.</summary>
    private IQueryable<StoreListing> Shelf(Guid organizationId) =>
        db.StoreListings.Where(listing =>
            listing.OrganizationId == organizationId && listing.Status == StoreListingStatus.Active);
}

/// <summary>Domain-or-id store resolution, off the web layer.</summary>
public sealed class EfStoreDirectory(HaulScoutDbContext db) : IStoreDirectory
{
    public async Task<Guid?> ResolveOrganizationIdAsync(string rawStoreValue, CancellationToken cancellationToken)
    {
        Guid organizationId;
        if (Guid.TryParse(rawStoreValue, out Guid id))
        {
            organizationId = await db.Organizations
                .Where(org => org.Id == id)
                .Select(org => org.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            string domain = Normalize(rawStoreValue);
            organizationId = await db.Organizations
                .Where(org => org.StoreDomain != null && org.StoreDomain.ToLower() == domain)
                .Select(org => org.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return organizationId == Guid.Empty ? null : organizationId;
    }

    private static string Normalize(string value)
    {
        string domain = value.ToLowerInvariant();
        if (domain.StartsWith("www.", StringComparison.Ordinal))
        {
            domain = domain[4..];
        }

        int colon = domain.IndexOf(':');
        return colon >= 0 ? domain[..colon] : domain;
    }
}
