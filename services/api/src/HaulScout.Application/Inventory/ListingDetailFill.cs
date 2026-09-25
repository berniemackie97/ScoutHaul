using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Takes what a marketplace listing says about a thing — its brand, colour, size, condition, description — and puts
/// it on the item the listing is linked to.
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
        changed |= Fill(listing.Description, value => item.Description = value, item.Description);
        changed |= Fill(listing.Brand, value => item.Brand = value, item.Brand);
        changed |= Fill(listing.Color, value => item.Color = value, item.Color);
        changed |= Fill(listing.Condition, value => item.Condition = value, item.Condition);
        changed |= Fill(listing.CategoryName, value => item.Category = value, item.Category);
        changed |= Fill(listing.Size, value => item.SizeValue = value, item.SizeValue);
        return changed;
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
