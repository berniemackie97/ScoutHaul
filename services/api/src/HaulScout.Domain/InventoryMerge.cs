using HaulScout.Domain.Entities;

namespace HaulScout.Domain;

/// <summary>
/// The rules for folding a duplicate record into the item it duplicates. The kept item's own answers stand; the
/// duplicate fills only what the kept one left blank, so a merge never overwrites something a person entered.
/// </summary>
public static class InventoryMerge
{
    /// <summary>Copies the duplicate's details into the kept item wherever the kept item has none.</summary>
    public static void FillBlanks(InventoryItem kept, InventoryItem duplicate)
    {
        kept.Description = Prefer(kept.Description, duplicate.Description);
        kept.Category = Prefer(kept.Category, duplicate.Category);
        kept.Brand = Prefer(kept.Brand, duplicate.Brand);
        kept.Model = Prefer(kept.Model, duplicate.Model);
        kept.Color = Prefer(kept.Color, duplicate.Color);
        kept.Condition = Prefer(kept.Condition, duplicate.Condition);
        if (string.IsNullOrWhiteSpace(kept.SizeSystem))
        {
            kept.SizeSystem = duplicate.SizeSystem;
            kept.SizeValue = duplicate.SizeValue;
        }

        kept.ManufacturerPartNumber = Prefer(kept.ManufacturerPartNumber, duplicate.ManufacturerPartNumber);
        kept.SerialNumber = Prefer(kept.SerialNumber, duplicate.SerialNumber);
        kept.CountryOfOrigin = Prefer(kept.CountryOfOrigin, duplicate.CountryOfOrigin);
        kept.ManufactureYear ??= duplicate.ManufactureYear;
        kept.AttributesJson = Prefer(kept.AttributesJson, duplicate.AttributesJson);
        kept.MeasurementsJson = Prefer(kept.MeasurementsJson, duplicate.MeasurementsJson);
        kept.StorageLocation = Prefer(kept.StorageLocation, duplicate.StorageLocation);
        kept.Barcode = Prefer(kept.Barcode, duplicate.Barcode);
        kept.PurchasePrice ??= duplicate.PurchasePrice;
        kept.EstimatedSalePrice ??= duplicate.EstimatedSalePrice;
        kept.SellerAskingPrice ??= duplicate.SellerAskingPrice;
        kept.PurchasedAt = Earliest(kept.PurchasedAt, duplicate.PurchasedAt);
        kept.ListedAt = Earliest(kept.ListedAt, duplicate.ListedAt);
        kept.SoldAt ??= duplicate.SoldAt;
        if (kept.Length is null && kept.Width is null && kept.Height is null)
        {
            (kept.Length, kept.Width, kept.Height, kept.DimensionUnit) = (duplicate.Length, duplicate.Width, duplicate.Height, duplicate.DimensionUnit);
        }

        if (kept.Weight is null)
        {
            (kept.Weight, kept.WeightUnit) = (duplicate.Weight, duplicate.WeightUnit);
        }

        kept.Accessories = Prefer(kept.Accessories, duplicate.Accessories);
        kept.MissingParts = Prefer(kept.MissingParts, duplicate.MissingParts);
        kept.TestingNotes = Prefer(kept.TestingNotes, duplicate.TestingNotes);
        kept.CleaningNotes = Prefer(kept.CleaningNotes, duplicate.CleaningNotes);
        kept.ListingNotes = Prefer(kept.ListingNotes, duplicate.ListingNotes);
        kept.Notes = Combine(kept.Notes, duplicate.Notes);
        if (kept.FunctionalStatus == InventoryFunctionalStatus.Unknown) kept.FunctionalStatus = duplicate.FunctionalStatus;
        if (kept.CompletenessStatus == InventoryCompletenessStatus.Unknown) kept.CompletenessStatus = duplicate.CompletenessStatus;
        if (kept.AuthenticityStatus == InventoryAuthenticityStatus.NotAssessed) kept.AuthenticityStatus = duplicate.AuthenticityStatus;
        if (kept.PackagingStatus == InventoryPackagingStatus.NotApplicable) kept.PackagingStatus = duplicate.PackagingStatus;
    }

    /// <summary>
    /// The stage the merged item is at: the further along of the two. A duplicate created by a sale import is the
    /// sold one, and the record kept for its photos and listings must say it sold, not that it is still listed.
    /// </summary>
    public static InventoryStatus MergedStatus(InventoryStatus kept, InventoryStatus duplicate) =>
        duplicate is not (InventoryStatus.Donated or InventoryStatus.Disposed)
        && InventoryLifecycle.Rank(duplicate) > InventoryLifecycle.Rank(kept)
            ? duplicate
            : kept;

    /// <summary>
    /// When both records are tracked on the same channel account, the row that says more about the listing wins:
    /// a live or sold listing over one that is not, then the one with a link, then the most recently synced.
    /// </summary>
    public static ChannelListing Preferred(ChannelListing kept, ChannelListing duplicate)
    {
        static int Weight(ChannelListing listing) => listing.Status switch
        {
            ChannelListingStatus.Sold => 4,
            ChannelListingStatus.Active or ChannelListingStatus.PendingDelist => 3,
            ChannelListingStatus.Draft or ChannelListingStatus.Error => 2,
            ChannelListingStatus.Delisted => 1,
            _ => 0,
        };

        int byStatus = Weight(duplicate).CompareTo(Weight(kept));
        if (byStatus != 0) return byStatus > 0 ? duplicate : kept;
        if ((kept.ExternalUrl is null) != (duplicate.ExternalUrl is null)) return kept.ExternalUrl is null ? duplicate : kept;
        return (duplicate.LastSyncedAt ?? DateTimeOffset.MinValue) > (kept.LastSyncedAt ?? DateTimeOffset.MinValue) ? duplicate : kept;
    }

    private static string? Prefer(string? kept, string? duplicate) => string.IsNullOrWhiteSpace(kept) ? duplicate : kept;

    private static string? Combine(string? kept, string? duplicate) =>
        string.IsNullOrWhiteSpace(duplicate) || (kept?.Contains(duplicate.Trim(), StringComparison.Ordinal) ?? false)
            ? kept
            : string.IsNullOrWhiteSpace(kept) ? duplicate : $"{kept.TrimEnd()}\n\n{duplicate.Trim()}";

    private static DateTimeOffset? Earliest(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null ? left : left < right ? left : right;
}
