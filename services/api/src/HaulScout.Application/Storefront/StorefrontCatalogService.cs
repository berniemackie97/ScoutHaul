using System.Text;
using HaulScout.Application.Contracts;
using HaulScout.Domain;

namespace HaulScout.Application.Storefront;

/// <summary>
/// The public catalog: what an anonymous shopper sees. Paging limits, category-slug matching, URL/slug construction
/// and the honest availability wording are decisions and live here; turning them into SQL lives behind the port.
/// </summary>
public sealed class StorefrontCatalogService(IStorefrontCatalogStore catalog)
{
    private const int MaxPageSize = 60;

    public async Task<StorefrontPageDto<StorefrontListingDto>> BrowseAsync(
        Guid organizationId,
        string? category,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Category URLs carry slugs; items carry free-text names. The mapping is decided here, against the real
        // names on the shelf, so an unknown slug is an empty page rather than a guess.
        string? categoryName = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            string slug = category.Trim();
            categoryName = (await catalog.ListCategoriesAsync(organizationId, cancellationToken))
                .Select(entry => entry.Name)
                .FirstOrDefault(name => Slugify(name) == slug);
            if (categoryName is null)
            {
                return new StorefrontPageDto<StorefrontListingDto>([], page, pageSize, 0);
            }
        }

        (IReadOnlyList<CatalogRow> rows, int total) = await catalog.BrowseAsync(
            organizationId, categoryName, TrimToNull(search), (page - 1) * pageSize, pageSize, cancellationToken);

        List<StorefrontListingDto> items = rows
            .Select(row => new StorefrontListingDto(
                row.ListingId, BuildSlug(row.Title, row.ListingId), row.Title, row.Subtitle, row.ListPrice,
                row.CompareAtPrice, row.IsFeatured, row.StoreCategoryId, row.Condition, row.Brand,
                PhotoUrl(row.HeroPhotoId), row.Category))
            .ToList();

        return new StorefrontPageDto<StorefrontListingDto>(items, page, pageSize, total);
    }

    public async Task<StorefrontListingDetailDto?> FindDetailAsync(Guid organizationId, string slug, CancellationToken cancellationToken)
    {
        if (!TryGetSlugId(slug, out Guid listingId))
        {
            return null;
        }

        CatalogDetailRow? row = await catalog.FindDetailAsync(organizationId, listingId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        List<string> gallery = row.PhotoIds.Select(id => PhotoUrl(id)!).ToList();
        CatalogItemSpecs item = row.Item;

        return new StorefrontListingDetailDto(
            row.ListingId, BuildSlug(row.Title, row.ListingId), row.Title, row.Subtitle, row.Description,
            row.ListPrice, row.CompareAtPrice, item.Brand, item.Condition,
            row.MetaTitle ?? row.Title, row.MetaDescription ?? row.Description,
            gallery.FirstOrDefault(), item.Category, item.Model, item.Color, item.SizeSystem, item.SizeValue,
            item.FunctionalStatus, item.CompletenessStatus, item.PackagingStatus, item.CountryOfOrigin,
            item.ManufactureYear, item.Length, item.Width, item.Height, item.DimensionUnit, item.Weight, item.WeightUnit,
            gallery,
            row.Status switch
            {
                StoreListingStatus.Reserved => "Reserved",
                StoreListingStatus.Sold => "Sold",
                _ => "Available",
            });
    }

    public async Task<IReadOnlyList<StoreCategoryDto>> ListCategoriesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        (await catalog.ListCategoriesAsync(organizationId, cancellationToken))
            .Select((entry, index) => new StoreCategoryDto(Guid.Empty, entry.Name, Slugify(entry.Name), null, null, index, true))
            .ToList();

    public Task<PublicPhoto?> FindPublicPhotoAsync(Guid photoId, bool fullSize, CancellationToken cancellationToken) =>
        catalog.FindPublicPhotoAsync(photoId, fullSize, cancellationToken);

    // Stable, unique public slug: "{title-slug}-{listingId:N}". The id suffix is the lookup key.
    internal static string BuildSlug(string title, Guid id) => $"{Slugify(title)}-{id:N}";

    internal static bool TryGetSlugId(string slug, out Guid id)
    {
        id = Guid.Empty;
        return !string.IsNullOrEmpty(slug)
            && slug.Length >= 32
            && Guid.TryParseExact(slug[^32..], "N", out id);
    }

    internal static string Slugify(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        string slug = builder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Trim('-');
    }

    private static string? PhotoUrl(Guid? photoId) => photoId is null ? null : $"/api/storefront/photos/{photoId}";

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
