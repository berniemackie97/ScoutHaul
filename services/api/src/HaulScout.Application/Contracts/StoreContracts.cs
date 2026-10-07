using HaulScout.Domain.Entities;
using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// ── Storefront catalog (operator-facing management) ──────────────

public sealed record StoreListingDto(
    Guid Id,
    Guid InventoryItemId,
    Guid? StoreCategoryId,
    string Slug,
    string Title,
    string? Subtitle,
    string? Description,
    decimal ListPrice,
    decimal? CompareAtPrice,
    bool AcceptsOffers,
    decimal? MinimumOfferAmount,
    decimal? MaximumOfferAmount,
    StoreShippingPolicy ShippingPolicy,
    decimal? ShippingAmountOverride,
    StoreListingStatus Status,
    bool IsFeatured,
    string? MetaTitle,
    string? MetaDescription,
    int ViewCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Publishes an existing InventoryItem onto the storefront.</summary>
public sealed record PublishListingRequest(
    Guid InventoryItemId,
    decimal ListPrice,
    Guid? StoreCategoryId,
    string? Title,
    string? Subtitle,
    string? Description,
    decimal? CompareAtPrice,
    bool IsFeatured,
    string? MetaTitle,
    string? MetaDescription);

public sealed record UpdateStoreListingRequest(
    decimal ListPrice,
    Guid? StoreCategoryId,
    string? Title,
    string? Subtitle,
    string? Description,
    decimal? CompareAtPrice,
    bool IsFeatured,
    string? MetaTitle,
    string? MetaDescription,
    StoreListingStatus Status);

public sealed record UpdateStoreListingCommerceRequest(
    bool AcceptsOffers,
    decimal? MinimumOfferAmount,
    decimal? MaximumOfferAmount,
    StoreShippingPolicy ShippingPolicy,
    decimal? ShippingAmountOverride);

// ── Store connection / settings (operator onboarding) ────────────

/// <summary>
/// The organization's storefront identity. <c>StoreDomain</c> is the public host the
/// storefront resolves the org by; <c>null</c> means no public storefront is connected.
/// </summary>
public sealed record StoreSettingsDto(string Name, string? StoreDomain, bool IsLive, string TimeZoneId = Organization.DefaultTimeZoneId);

/// <summary>
/// Sets the organization's storefront identity. The domain is normalized and validated, and
/// must be globally unique (it is the storefront resolution key). A blank domain disconnects
/// the storefront.
/// </summary>
/// <param name="TimeZoneId">IANA zone the business keeps its days in. Null leaves it unchanged.</param>
public sealed record UpdateStoreSettingsRequest(string Name, string? StoreDomain, string? TimeZoneId = null);

public sealed record StoreCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentId,
    int SortOrder,
    bool IsActive);

public sealed record UpsertStoreCategoryRequest(
    string Name,
    string? Slug,
    string? Description,
    Guid? ParentId,
    int SortOrder,
    bool IsActive);

// ── Public storefront (customer-facing, anonymous) ───────────────
// Deliberately a safe subset — never exposes cost, supplier, or internal data.

public sealed record StorefrontListingDto(
    Guid Id,
    string Slug,
    string Title,
    string? Subtitle,
    decimal ListPrice,
    decimal? CompareAtPrice,
    bool AcceptsOffers,
    decimal? MinimumOfferAmount,
    decimal? MaximumOfferAmount,
    StoreShippingPolicy ShippingPolicy,
    decimal? ShippingAmountOverride,
    bool IsFeatured,
    Guid? StoreCategoryId,
    string? Condition,
    string? Brand,
    // Relative URL of the hero photo (e.g. /api/storefront/photos/{id}); null when no photo.
    string? ImageUrl,
    // Category name (free-text from the inventory item).
    string? Category);

public sealed record StorefrontListingDetailDto(
    Guid Id,
    string Slug,
    string Title,
    string? Subtitle,
    string? Description,
    decimal ListPrice,
    decimal? CompareAtPrice,
    bool AcceptsOffers,
    decimal? MinimumOfferAmount,
    decimal? MaximumOfferAmount,
    StoreShippingPolicy ShippingPolicy,
    decimal? ShippingAmountOverride,
    string? Brand,
    string? Condition,
    string? MetaTitle,
    string? MetaDescription,
    // Relative URL of the hero photo (e.g. /api/storefront/photos/{id}); null when no photo.
    string? ImageUrl,
    // Category name (free-text from the inventory item).
    string? Category,
    string? Model,
    IReadOnlyList<string> Colors,
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
    WeightUnit WeightUnit,
    // All photos for the gallery, hero first (relative /api/storefront/photos/{id} URLs). Empty when none.
    IReadOnlyList<string> ImageUrls = default!,
    // "Available", "Reserved" (in someone's checkout), or "Sold" — the PDP renders the honest state instead of 404ing.
    string Availability = "Available");

/// <summary>What one reconcile pass did — the backfill/repair for shelf-vs-inventory drift.</summary>
public sealed record ReconcileStoreCatalogResultDto(
    int ItemsExamined,
    int ListingsCreated,
    int NowActive,
    int AwaitingPhotos,
    int Retired);

public sealed record StorefrontPageDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total);
