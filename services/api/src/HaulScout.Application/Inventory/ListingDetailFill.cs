using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Takes what a marketplace listing says about a thing — its brand, colour, size, condition, description — and puts
/// it on the item the listing is linked to, in HaulScout's own terms: "Tops" is filed as Clothing, "ug" is Good,
/// "L / US 12-14" is US letter L. What cannot be read that way is left for a person rather than stored raw.
///
/// Only blanks are filled. A person who typed a description, or corrected a brand the marketplace got wrong, has
/// said something the listing has not; a sync that overwrote it would be destroying work to copy in a worse answer.
/// That rule also makes this safe to run as often as anything changes, and safe when an item is listed in several
/// places at once: whichever listing is read first fills a blank, and the rest find nothing left to fill.
/// </summary>
public static class ListingDetailFill
{
    public static bool Apply(InventoryItem item, PlatformListing listing)
    {
        bool changed = false;
        changed |= Fill(ListingDetailReading.Description(listing), value => item.Description = value, item.Description);
        changed |= Fill(ListingDetailReading.Brand(listing), value => item.Brand = value, item.Brand);
        changed |= FillColors(item, listing);
        changed |= Fill(ListingDetailReading.Condition(listing), value => item.Condition = value, item.Condition);
        changed |= Fill(ListingDetailReading.Category(listing), value => item.Category = value, item.Category);
        changed |= FillSize(item, listing);
        return changed;
    }

    /// <summary>A listing names colors its own way ("Grey, Off-White"); the item takes them in the standard list.</summary>
    private static bool FillColors(InventoryItem item, PlatformListing listing)
    {
        if (item.Colors.Count > 0)
        {
            return false;
        }

        IReadOnlyList<string> colors = ListingDetailReading.Colors(listing);
        if (colors.Count == 0)
        {
            return false;
        }

        item.Colors = [.. colors];
        return true;
    }

    /// <summary>A size goes in only with the system it is measured in, read against the item's (just filled) category.</summary>
    private static bool FillSize(InventoryItem item, PlatformListing listing)
    {
        if (!string.IsNullOrWhiteSpace(item.SizeSystem) || !string.IsNullOrWhiteSpace(item.SizeValue)
            || ListingDetailReading.Size(listing, ItemProfiles.For(item.Category)) is not { } size)
        {
            return false;
        }

        (item.SizeSystem, item.SizeValue) = size;
        return true;
    }

    private static bool Fill(string? offered, Action<string> set, string? current)
    {
        if (!string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(offered))
        {
            return false;
        }

        set(offered.Trim());
        return true;
    }
}
