using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HaulScout.Application.Contracts;
using HaulScout.Domain;

namespace HaulScout.Application.Imports;

/// <summary>
/// Poshmark's inventory report turned into listing snapshots.
///
/// The report lists every listing that is available, as of the date in its preamble — so it is a
/// complete picture: a listing HaulScout saw before and this report no longer has is not live any
/// more. It carries no Poshmark listing id, so each listing is keyed from its listing date, title,
/// brand, size and colour. Renaming a listing between two imports therefore reads as a new listing;
/// the sync agent, which sees Poshmark's real ids, removes that limit.
/// </summary>
public static partial class PoshmarkInventoryReportParser
{
    public const string ListingDate = "Listing Date";
    public const string ListingSku = "Listing SKU";
    public const string ItemSku = "Item SKU";
    public const string Title = "Listing Title";
    public const string Department = "Department";
    public const string Category = "Category";
    public const string Subcategory = "Subcategory";
    public const string Brand = "Brand";
    public const string Color = "Color";
    public const string Size = "Size";
    public const string NewWithTags = "NWT";
    public const string QuantityAvailable = "Quantity Available";
    public const string CurrentPrice = "Current Listing Price";

    public static readonly string[] Signature = [ListingDate, Title, QuantityAvailable, CurrentPrice, NewWithTags];

    public sealed record RowIssue(int RowNumber, string? Reference, string Message, bool Rejected);

    public sealed record ParseResult(
        IReadOnlyList<IngestPlatformListingInput> Listings,
        IReadOnlyList<RowIssue> Issues,
        int RowsRead,
        DateTimeOffset? AsOf);

    public static bool Matches(string csv) => CsvTable.Load(csv, Signature) is not null;

    public static ParseResult Parse(string csv, TimeZoneInfo zone)
    {
        CsvTable? table = CsvTable.Load(csv, Signature);
        if (table is null)
        {
            return new ParseResult([], [new RowIssue(0, null, "This is not a Poshmark inventory report.", true)], 0, null);
        }

        DateTimeOffset? asOf = ReadAsOf(csv, zone);
        List<IngestPlatformListingInput> listings = [];
        List<RowIssue> issues = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int rowNumber = 0;

        foreach (IReadOnlyList<string> row in table.Rows)
        {
            rowNumber++;
            string title = table.Text(row, Title);
            if (title.Length == 0)
            {
                issues.Add(new RowIssue(rowNumber, null, "No listing title.", true));
                continue;
            }

            if (!table.TryMoney(row, CurrentPrice, out decimal? price) || price is null)
            {
                issues.Add(new RowIssue(rowNumber, title, $"'{CurrentPrice}' is missing or unreadable ('{table.Text(row, CurrentPrice)}').", true));
                continue;
            }

            string listedText = table.Text(row, ListingDate);
            DateTimeOffset? listedAt = LocalDate(listedText, zone);
            string brand = table.Meaningful(row, Brand) ?? string.Empty;
            string size = table.Meaningful(row, Size) ?? string.Empty;
            string color = table.Meaningful(row, Color) ?? string.Empty;

            string id = "poshmark-" + Hash($"{listedText}|{title}|{brand}|{size}|{color}");
            if (!seen.Add(id))
            {
                issues.Add(new RowIssue(rowNumber, title, "The same listing appears twice in this report; the second copy was ignored.", false));
                continue;
            }

            string category = string.Join(" › ", new[]
            {
                table.Meaningful(row, Department), table.Meaningful(row, Category), table.Meaningful(row, Subcategory),
            }.Where(part => part is not null));

            listings.Add(new IngestPlatformListingInput(
                ExternalListingId: id,
                Title: title,
                Price: price.Value,
                // Every row in this report is, by Poshmark's own note, a listing that is available.
                Status: PlatformListingStatus.Active,
                Quantity: Math.Max(1, table.Int(row, QuantityAvailable, 1)),
                Platform: "Poshmark",
                Currency: "USD",
                Sku: table.Meaningful(row, ItemSku) ?? table.Meaningful(row, ListingSku),
                CategoryName: category.Length == 0 ? null : category,
                Condition: string.Equals(table.Meaningful(row, NewWithTags), "Y", StringComparison.OrdinalIgnoreCase) ? "New with tags" : null,
                ListedAt: listedAt,
                RawJson: JsonSerializer.Serialize(table.Header
                    .Select((header, index) => (header, value: index < row.Count ? row[index] : string.Empty))
                    .DistinctBy(pair => pair.header)
                    .ToDictionary(pair => pair.header, pair => pair.value))));
        }

        if (asOf is null)
        {
            issues.Add(new RowIssue(0, null, "The report does not say which date it describes; today's date is assumed.", false));
        }

        return new ParseResult(listings, issues, rowNumber, asOf);
    }

    /// <summary>"Inventory as of 07/09/2026" from the preamble, as the start of that day locally.</summary>
    private static DateTimeOffset? ReadAsOf(string csv, TimeZoneInfo zone)
    {
        Match match = AsOfLine().Match(csv.Length > 2000 ? csv[..2000] : csv);
        return match.Success ? LocalDate(match.Groups[1].Value, zone) : null;
    }

    private static DateTimeOffset? LocalDate(string text, TimeZoneInfo zone)
    {
        if (!DateTime.TryParseExact(text.Trim(), ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
        {
            return null;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()))).ToLowerInvariant()[..20];

    [GeneratedRegex(@"Inventory as of\s+(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex AsOfLine();
}
