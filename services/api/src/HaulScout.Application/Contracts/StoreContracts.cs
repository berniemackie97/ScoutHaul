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

// ── Store connection / settings (operator onboarding) ────────────

/// <summary>
/// The organization's storefront identity. <c>StoreDomain</c> is the public host the
/// storefront resolves the org by; <c>null</c> means no public storefront is connected.
/// </summary>
public sealed record StoreSettingsDto(string Name, string? StoreDomain, bool IsLive);

/// <summary>
/// Sets the organization's storefront identity. The domain is normalized and validated, and
/// must be globally unique (it is the storefront resolution key). A blank domain disconnects
/// the storefront.
/// </summary>
public sealed record UpdateStoreSettingsRequest(string Name, string? StoreDomain);

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
    string? Brand,
    string? Condition,
    string? MetaTitle,
    string? MetaDescription,
    // Relative URL of the hero photo (e.g. /api/storefront/photos/{id}); null when no photo.
    string? ImageUrl,
    // Category name (free-text from the inventory item).
    string? Category);

public sealed record StorefrontPageDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total);
