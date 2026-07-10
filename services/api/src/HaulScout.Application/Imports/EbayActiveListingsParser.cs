using System.Text.Json;
using HaulScout.Application.Contracts;
using HaulScout.Domain;

namespace HaulScout.Application.Imports;

/// <summary>
/// Parses eBay's Seller Hub "Active Listings" export into normalized listing rows. This is how legacy listings —
/// ones created on eBay before ScoutHaul existed — seed or link inventory: each row carries the eBay item id, the
/// title, current price, available/sold quantity, category, condition, and format. Reconciliation then links each
/// to an inventory item (or creates a draft). Header-name addressed, so an added/reordered column can't corrupt it.
/// </summary>
public static class EbayActiveListingsParser
{
    public const string ItemNumber = "Item number";
    public const string Title = "Title";

    public sealed record ParseResult(
        IReadOnlyList<IngestPlatformListingInput> Listings,
        int RowsParsed,
        int RowsSkipped,
        string? Warning);

    public static bool Matches(string csv) =>
        CsvTable.Load(csv, ItemNumber, Title, "Available quantity") is not null;

    public static ParseResult Parse(string csv)
    {
        CsvTable? table = CsvTable.Load(csv, ItemNumber, Title, "Available quantity");
        if (table is null)
        {
            return new ParseResult([], 0, 0, "This does not look like an eBay Active Listings report.");
        }

        var listings = new List<IngestPlatformListingInput>();
        int skipped = 0;
        foreach (var row in table.Rows)
        {
            string itemNumber = table.Text(row, ItemNumber);
            string title = table.Text(row, Title);
            if (itemNumber.Length == 0 || title.Length == 0)
            {
                skipped++; // trailer / blank row
                continue;
            }

            // "Current price" is what it is listed at now; "Start price" is the fallback for fixed-price listings.
            decimal price = table.Money(row, "Current price");
            if (price == 0)
            {
                price = table.Money(row, "Start price");
            }

            listings.Add(new IngestPlatformListingInput(
                ExternalListingId: itemNumber,
                Title: title,
                Price: price,
                Status: PlatformListingStatus.Active,
                Quantity: Math.Max(0, table.Int(row, "Available quantity", 1)),
                QuantitySold: table.Int(row, "Sold quantity", 0),
                Platform: "eBay",
                Currency: table.TextOrNull(row, "Currency") ?? "USD",
                Format: table.TextOrNull(row, "Format"),
                Sku: table.TextOrNull(row, "Custom label (SKU)"),
                CategoryName: table.TextOrNull(row, "eBay category 1 name"),
                Condition: table.TextOrNull(row, "Condition"),
                Url: $"https://www.ebay.com/itm/{itemNumber}",
                ListedAt: table.Date(row, "Start date"),
                EndsAt: table.Date(row, "End date"),
                RawJson: JsonSerializer.Serialize(new { itemNumber })));
        }

        string? warning = listings.Count == 0
            ? "No active-listing rows were found in the report."
            : skipped > 0 ? $"{skipped} row(s) without an item number or title were skipped." : null;
        return new ParseResult(listings, listings.Count, skipped, warning);
    }
}
