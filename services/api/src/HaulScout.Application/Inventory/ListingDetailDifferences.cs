using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>The item details a marketplace listing can disagree about.</summary>
public enum ListingDetailField
{
    Category,
    Condition,
    Brand,
    Colors,
    Size,
}

/// <summary>What the item says against what a linked listing says, both in HaulScout's terms.</summary>
public sealed record ListingDetailDifference(ListingDetailField Field, string Current, string Incoming, string? Raw);

/// <summary>
/// Where a linked listing says something different from the item. A sync only fills blanks
/// (<see cref="ListingDetailFill"/>), so a value that differs waits here for a person to choose; nothing applies
/// on its own. The listing's words are read exactly as the fill reads them, so a spelling the vocabulary already
/// folds ("Womens", "NAVY BLUE") never shows as a difference.
/// </summary>
public static class ListingDetailDifferences
{
    public static IReadOnlyList<ListingDetailDifference> Find(InventoryItem item, PlatformListing listing)
    {
        List<ListingDetailDifference> found = [];
        foreach (ListingDetailField field in Enum.GetValues<ListingDetailField>())
        {
            string current = Current(item, field);
            string? incoming = Incoming(item, listing, field);
            if (current.Length > 0 && incoming is not null && !string.Equals(current, incoming, StringComparison.OrdinalIgnoreCase))
            {
                string? raw = RawText(listing, field);
                found.Add(new ListingDetailDifference(field, current, incoming, string.Equals(raw, incoming, StringComparison.OrdinalIgnoreCase) ? null : raw));
            }
        }

        return found;
    }

    /// <summary>
    /// Takes the listing's value for one field. A new category also empties what the new kind of item has no field
    /// for, exactly as saving the editor would. False when the listing no longer says anything usable for it.
    /// </summary>
    public static bool Apply(InventoryItem item, PlatformListing listing, ListingDetailField field)
    {
        switch (field)
        {
            case ListingDetailField.Category when ListingDetailReading.Category(listing) is { } category:
                item.Category = category;
                break;
            case ListingDetailField.Condition when ListingDetailReading.Condition(listing) is { } condition:
                item.Condition = condition;
                break;
            case ListingDetailField.Brand when ListingDetailReading.Brand(listing) is { } brand:
                item.Brand = brand;
                break;
            case ListingDetailField.Colors when ListingDetailReading.Colors(listing) is { Count: > 0 } colors:
                item.Colors = [.. colors];
                break;
            case ListingDetailField.Size when ListingDetailReading.Size(listing, ItemProfiles.For(item.Category)) is { } size:
                (item.SizeSystem, item.SizeValue) = size;
                break;
            default:
                return false;
        }

        ItemProfiles.ClearInapplicable(item);
        return true;
    }

    private static string Current(InventoryItem item, ListingDetailField field) => field switch
    {
        ListingDetailField.Category => item.Category ?? string.Empty,
        ListingDetailField.Condition => item.Condition ?? string.Empty,
        ListingDetailField.Brand => item.Brand ?? string.Empty,
        ListingDetailField.Colors => string.Join(", ", item.Colors),
        ListingDetailField.Size => SizeText(item.SizeSystem, item.SizeValue),
        _ => string.Empty,
    };

    private static string? Incoming(InventoryItem item, PlatformListing listing, ListingDetailField field) => field switch
    {
        ListingDetailField.Category => ListingDetailReading.Category(listing),
        ListingDetailField.Condition => ListingDetailReading.Condition(listing),
        ListingDetailField.Brand => ListingDetailReading.Brand(listing),
        ListingDetailField.Colors => ListingDetailReading.Colors(listing) is { Count: > 0 } colors ? string.Join(", ", colors) : null,
        ListingDetailField.Size => ListingDetailReading.Size(listing, ItemProfiles.For(item.Category)) is { } size ? SizeText(size.System, size.Value) : null,
        _ => null,
    };

    private static string? RawText(PlatformListing listing, ListingDetailField field) => (field switch
    {
        ListingDetailField.Category => listing.CategoryName,
        ListingDetailField.Condition => listing.Condition,
        ListingDetailField.Brand => listing.Brand,
        ListingDetailField.Colors => listing.Color,
        ListingDetailField.Size => listing.Size,
        _ => null,
    })?.Trim();

    private static string SizeText(string? system, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : string.IsNullOrWhiteSpace(system) ? value : $"{value} ({system})";
}
