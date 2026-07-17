using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

public static class StoreMapping
{
    public static StoreListingDto ToDto(this StoreListing listing) => new(
        listing.Id,
        listing.InventoryItemId,
        listing.StoreCategoryId,
        listing.Slug,
        listing.Title,
        listing.Subtitle,
        listing.Description,
        listing.ListPrice,
        listing.CompareAtPrice,
        listing.Status,
        listing.IsFeatured,
        listing.MetaTitle,
        listing.MetaDescription,
        listing.ViewCount,
        listing.PublishedAt,
        listing.CreatedAt,
        listing.UpdatedAt);

    public static StoreCategoryDto ToDto(this StoreCategory category) => new(
        category.Id,
        category.Name,
        category.Slug,
        category.Description,
        category.ParentId,
        category.SortOrder,
        category.IsActive);
}
