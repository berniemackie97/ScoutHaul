using HaulScout.Domain;

namespace HaulScout.Application.Storefront;

/// <summary>What the public catalog asks of persistence. Filtering, search and paging must run as SQL, so the port
/// takes intent as data and returns flat projections; the Application composes them into the public DTOs.</summary>
public interface IStorefrontCatalogStore
{
    /// <summary>One page of the shelf (Active listings), newest/featured first, already filtered and searched.</summary>
    Task<(IReadOnlyList<CatalogRow> Rows, int Total)> BrowseAsync(
        Guid organizationId,
        string? categoryName,
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>A publicly visible listing (Active/Reserved/Sold — never Draft/Archived) with its item's specs.</summary>
    Task<CatalogDetailRow?> FindDetailAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken);

    /// <summary>Distinct item category names across the shelf, with counts, most stocked first.</summary>
    Task<IReadOnlyList<(string Name, int Count)>> ListCategoriesAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>A photo that is public because its item's listing is public; null otherwise. Org-agnostic by design —
    /// photo ids are unguessable and browsers fetch them without the store header.</summary>
    Task<PublicPhoto?> FindPublicPhotoAsync(Guid photoId, bool fullSize, CancellationToken cancellationToken);
}

public sealed record CatalogRow(
    Guid ListingId,
    string Title,
    string? Subtitle,
    decimal ListPrice,
    decimal? CompareAtPrice,
    bool IsFeatured,
    Guid? StoreCategoryId,
    string? Condition,
    string? Brand,
    string? Category,
    Guid? HeroPhotoId);

public sealed record CatalogDetailRow(
    Guid ListingId,
    string Title,
    string? Subtitle,
    string? Description,
    decimal ListPrice,
    decimal? CompareAtPrice,
    string? MetaTitle,
    string? MetaDescription,
    StoreListingStatus Status,
    CatalogItemSpecs Item,
    IReadOnlyList<Guid> PhotoIds);

public sealed record CatalogItemSpecs(
    string? Brand,
    string? Model,
    string? Category,
    string? Color,
    string? Condition,
    string? SizeSystem,
    string? SizeValue,
    InventoryFunctionalStatus FunctionalStatus,
    InventoryCompletenessStatus CompletenessStatus,
    InventoryPackagingStatus PackagingStatus,
    string? CountryOfOrigin,
    int? ManufactureYear,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    DimensionUnit DimensionUnit,
    decimal? Weight,
    WeightUnit WeightUnit);

public sealed record PublicPhoto(byte[] Bytes, string ContentType);
