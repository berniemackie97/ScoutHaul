using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>A marketplace listing's details read in HaulScout's terms; null where a value cannot be read that way.</summary>
public static class ListingDetailReading
{
    public static string? Brand(PlatformListing listing) => Text(listing.Brand);

    public static string? Description(PlatformListing listing) => Text(listing.Description);

    public static string? Condition(PlatformListing listing) => ItemConditions.Standard.Canonical(listing.Condition);

    public static string? Category(PlatformListing listing) => ItemCategories.Canonical(ItemCategories.Standard, listing.CategoryName);

    public static IReadOnlyList<string> Colors(PlatformListing listing) => ItemColors.Standard.CanonicalList([listing.Color], ItemColors.Max);

    /// <summary>A size, only with the system it is measured in, read against the profile of the item it would go on.</summary>
    public static (string System, string Value)? Size(PlatformListing listing, ItemProfile profile) => ItemSizes.Read(listing.Size, profile);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
