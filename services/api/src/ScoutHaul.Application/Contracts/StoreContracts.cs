using ScoutHaul.Domain;

namespace ScoutHaul.Application.Contracts;

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
