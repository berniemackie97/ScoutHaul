using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HaulScout.Application.Imports;

public static partial class EbayListingQualityWorkbookParser
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public sealed record ListingQualityRow(
        string ExternalListingId,
        string Title,
        string? CategoryName,
        string? ListingCondition,
        string? PriceRange,
        DateTimeOffset ReportGeneratedAt,
        string? Action,
        decimal? DailyImpressions,
        decimal? ClickThroughRate,
        decimal? SalesConversionRate,
        decimal? PromotedListingsAdRate,
        int? RecommendedItemSpecificsFilled,
        string? RecommendedItemSpecificsToAdd,
        string? Brand,
        string? Mpn,
        string? Upc,
        int? PhotoCount,
        int? TitleKeywordCount,
        bool? FreeShipping,
        int? EstimatedHandlingDays,
        bool? AcceptsReturns,
        bool? SellerPaidReturns,
        bool? PromotedListings,
        bool? BestOffer,
        IReadOnlyDictionary<string, string> RawFields);

    public sealed record ParseResult(IReadOnlyList<ListingQualityRow> Rows, IReadOnlyList<string> Warnings);

    public static ParseResult Parse(byte[] workbookBytes, DateTimeOffset fallbackReportDate)
    {
        if (workbookBytes.Length == 0)
        {
            return new ParseResult([], ["The Listing Quality workbook was empty."]);
        }

        using MemoryStream stream = new(workbookBytes);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);

        if (archive.GetEntry("xl/workbook.xml") is null)
        {
            return new ParseResult([], ["This is not an Excel workbook exported by eBay Listing Quality."]);
        }

        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        Dictionary<string, string> workbookRelationships = ReadRelationships(archive, "xl/_rels/workbook.xml.rels");

        List<ListingQualityRow> rows = [];
        List<string> warnings = [];
        foreach ((string sheetName, string sheetPath) in ReadSheets(archive, workbookRelationships))
        {
            if (IsNonDataSheet(sheetName))
            {
                continue;
            }

            ZipArchiveEntry? sheetEntry = archive.GetEntry(sheetPath);
            if (sheetEntry is null)
            {
                warnings.Add($"Skipped worksheet '{sheetName}' because its data part was missing.");
                continue;
            }

            XDocument sheet = XDocument.Load(sheetEntry.Open());
            Dictionary<string, string> hyperlinkTargets = ReadWorksheetHyperlinks(archive, sheetPath, sheet);
            List<WorksheetRow> worksheetRows = ReadRows(sheet, sharedStrings);
            if (worksheetRows.Count == 0)
            {
                continue;
            }

            SheetMetadata metadata = ReadMetadata(worksheetRows, fallbackReportDate);
            WorksheetRow? headerRow = worksheetRows.FirstOrDefault(row => HasValue(row, "Item title") && HasValue(row, "Action"));
            if (headerRow is null)
            {
                continue;
            }

            Dictionary<int, string> headers = headerRow.Cells
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Value))
                .ToDictionary(cell => cell.Column, cell => cell.Value.Trim());

            int? titleColumn = ColumnFor(headers, "Item title");
            if (titleColumn is null)
            {
                continue;
            }

            int blankRun = 0;
            foreach (WorksheetRow dataRow in worksheetRows.Where(row => row.Number > headerRow.Number))
            {
                string title = dataRow.ValueAt(titleColumn.Value).Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    blankRun++;
                    if (blankRun >= 3)
                    {
                        break;
                    }
                    continue;
                }
                blankRun = 0;

                string? hyperlink = hyperlinkTargets.GetValueOrDefault(CellReference(titleColumn.Value, dataRow.Number));
                string? externalListingId = ExtractListingId(hyperlink);
                if (string.IsNullOrWhiteSpace(externalListingId))
                {
                    warnings.Add($"Skipped listing quality row '{title}' because eBay did not expose a listing id hyperlink.");
                    continue;
                }

                Dictionary<string, string> raw = BuildRawFields(headers, dataRow);
                rows.Add(new ListingQualityRow(
                    externalListingId,
                    title,
                    metadata.CategoryName,
                    metadata.ListingCondition,
                    metadata.PriceRange,
                    metadata.ReportGeneratedAt,
                    Value(raw, "Action"),
                    Decimal(raw, "Daily impressions per listing"),
                    Decimal(raw, "Click-through rate"),
                    Decimal(raw, "Sales conversion rate"),
                    Decimal(raw, "Promoted listings ad rate"),
                    Integer(raw, "Recommended item specifics filled"),
                    Value(raw, "Recommended item specifics to add"),
                    Value(raw, "Brand"),
                    Value(raw, "MPN"),
                    Value(raw, "UPC"),
                    Integer(raw, "Number of photos"),
                    Integer(raw, "Number of keywords in title"),
                    Enabled(raw, "Free shipping"),
                    Integer(raw, "Estimated handling time"),
                    Enabled(raw, "Accepts returns"),
                    Enabled(raw, "Seller paid returns"),
                    Enabled(raw, "Promoted listings"),
                    Enabled(raw, "Best Offer"),
                    raw));
            }
        }

        if (rows.Count == 0 && warnings.Count == 0)
        {
            warnings.Add("No listing-level recommendations were found in the eBay Listing Quality workbook.");
        }

        return new ParseResult(rows, warnings);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        XDocument document = XDocument.Load(entry.Open());
        return document.Descendants(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToList();
    }

    private static IReadOnlyList<(string Name, string Path)> ReadSheets(ZipArchive archive, IReadOnlyDictionary<string, string> workbookRelationships)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/workbook.xml");
        if (entry is null)
        {
            return [];
        }

        XDocument workbook = XDocument.Load(entry.Open());
        List<(string, string)> sheets = [];
        foreach (XElement sheet in workbook.Descendants(SpreadsheetNs + "sheet"))
        {
            string? name = sheet.Attribute("name")?.Value;
            string? relationshipId = sheet.Attribute(RelationshipNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(relationshipId)
                || !workbookRelationships.TryGetValue(relationshipId, out string? target))
            {
                continue;
            }
            sheets.Add((name, NormalizeWorkbookTarget(target)));
        }
        return sheets;
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive archive, string path)
    {
        ZipArchiveEntry? entry = archive.GetEntry(path);
        if (entry is null)
        {
            return [];
        }

        XDocument document = XDocument.Load(entry.Open());
        return document.Root?.Elements(PackageRelationshipNs + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(element => element.Attribute("Id")!.Value, element => element.Attribute("Target")!.Value)
            ?? [];
    }

    private static Dictionary<string, string> ReadWorksheetHyperlinks(ZipArchive archive, string sheetPath, XDocument sheet)
    {
        string relationshipsPath = WorksheetRelationshipsPath(sheetPath);
        Dictionary<string, string> relationships = ReadRelationships(archive, relationshipsPath);
        Dictionary<string, string> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement hyperlink in sheet.Descendants(SpreadsheetNs + "hyperlink"))
        {
            string? cellRef = hyperlink.Attribute("ref")?.Value;
            string? relationshipId = hyperlink.Attribute(RelationshipNs + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(cellRef)
                && !string.IsNullOrWhiteSpace(relationshipId)
                && relationships.TryGetValue(relationshipId, out string? target))
            {
                targets[cellRef] = target;
            }
        }
        return targets;
    }

    private static List<WorksheetRow> ReadRows(XDocument sheet, IReadOnlyList<string> sharedStrings)
    {
        List<WorksheetRow> rows = [];
        foreach (XElement row in sheet.Descendants(SpreadsheetNs + "row"))
        {
            if (!int.TryParse(row.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rowNumber))
            {
                continue;
            }

            List<WorksheetCell> cells = [];
            foreach (XElement cell in row.Elements(SpreadsheetNs + "c"))
            {
                string? reference = cell.Attribute("r")?.Value;
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                int column = ColumnIndex(reference);
                string value = ReadCellValue(cell, sharedStrings);
                cells.Add(new WorksheetCell(column, value));
            }

            rows.Add(new WorksheetRow(rowNumber, cells));
        }
        return rows;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        string? raw = cell.Element(SpreadsheetNs + "v")?.Value;
        if (cell.Attribute("t")?.Value == "s"
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sharedIndex)
            && sharedIndex >= 0
            && sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        if (cell.Attribute("t")?.Value == "inlineStr")
        {
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));
        }

        return raw ?? string.Empty;
    }

    private static SheetMetadata ReadMetadata(IReadOnlyList<WorksheetRow> rows, DateTimeOffset fallbackReportDate)
    {
        string? categoryLine = rows.SelectMany(row => row.Cells).Select(cell => cell.Value).FirstOrDefault(value => value.StartsWith("Category:", StringComparison.OrdinalIgnoreCase));
        string? reportLine = rows.SelectMany(row => row.Cells).Select(cell => cell.Value).FirstOrDefault(value => value.StartsWith("Report generated:", StringComparison.OrdinalIgnoreCase));

        string? categoryName = null;
        string? condition = null;
        string? priceRange = null;
        if (!string.IsNullOrWhiteSpace(categoryLine))
        {
            Match match = CategoryLineRegex().Match(categoryLine);
            if (match.Success)
            {
                categoryName = TrimToNull(match.Groups["category"].Value);
                condition = TrimToNull(match.Groups["condition"].Value);
                priceRange = TrimToNull(match.Groups["price"].Value);
            }
        }

        return new SheetMetadata(categoryName, condition, priceRange, ParseReportDate(reportLine) ?? fallbackReportDate);
    }

    private static DateTimeOffset? ParseReportDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = ReportDateRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        int month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        int day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        int year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        int hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        int minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(-8));
    }

    private static bool HasValue(WorksheetRow row, string value) =>
        row.Cells.Any(cell => string.Equals(cell.Value.Trim(), value, StringComparison.OrdinalIgnoreCase));

    private static int? ColumnFor(IReadOnlyDictionary<int, string> headers, string header) =>
        headers.FirstOrDefault(pair => string.Equals(pair.Value, header, StringComparison.OrdinalIgnoreCase)).Key is int key && key > 0 ? key : null;

    private static Dictionary<string, string> BuildRawFields(IReadOnlyDictionary<int, string> headers, WorksheetRow dataRow)
    {
        Dictionary<string, string> raw = new(StringComparer.OrdinalIgnoreCase);
        foreach ((int column, string header) in headers)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }
            raw[header] = dataRow.ValueAt(column).Trim();
        }
        return raw;
    }

    private static string? ExtractListingId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        Match match = ListingIdRegex().Match(url);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static string? Value(IReadOnlyDictionary<string, string> raw, string key)
    {
        if (!raw.TryGetValue(key, out string? value))
        {
            return null;
        }
        return TrimToNull(value);
    }

    private static decimal? Decimal(IReadOnlyDictionary<string, string> raw, string key)
    {
        string? value = Value(raw, key);
        if (value is null || IsPlaceholder(value))
        {
            return null;
        }
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result) ? result : null;
    }

    private static int? Integer(IReadOnlyDictionary<string, string> raw, string key)
    {
        string? value = Value(raw, key);
        if (value is null || IsPlaceholder(value))
        {
            return null;
        }
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;
    }

    private static bool? Enabled(IReadOnlyDictionary<string, string> raw, string key)
    {
        string? value = Value(raw, key);
        if (value is null || IsPlaceholder(value))
        {
            return null;
        }
        if (value is "✔" or "Yes" or "Enabled")
        {
            return true;
        }
        if (value.Equals("Not enabled", StringComparison.OrdinalIgnoreCase)
            || value.Equals("No", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return null;
    }

    private static bool IsPlaceholder(string value) =>
        value.Equals("Not provided", StringComparison.OrdinalIgnoreCase)
        || value.Equals("No clicks", StringComparison.OrdinalIgnoreCase)
        || value.Equals("No sales", StringComparison.OrdinalIgnoreCase);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsNonDataSheet(string sheetName) =>
        sheetName.Equals("Summary", StringComparison.OrdinalIgnoreCase)
        || sheetName.Equals("Guide", StringComparison.OrdinalIgnoreCase)
        || sheetName.Equals("Google Shopping Rejections", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWorkbookTarget(string target)
    {
        string normalized = target.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? normalized : $"xl/{normalized}";
    }

    private static string WorksheetRelationshipsPath(string worksheetPath)
    {
        int slash = worksheetPath.LastIndexOf('/');
        string directory = slash >= 0 ? worksheetPath[..slash] : string.Empty;
        string file = slash >= 0 ? worksheetPath[(slash + 1)..] : worksheetPath;
        return $"{directory}/_rels/{file}.rels";
    }

    private static int ColumnIndex(string cellReference)
    {
        int column = 0;
        foreach (char character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }
            column = (column * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return column;
    }

    private static string CellReference(int column, int row)
    {
        string letters = string.Empty;
        int value = column;
        while (value > 0)
        {
            value--;
            letters = (char)('A' + (value % 26)) + letters;
            value /= 26;
        }
        return $"{letters}{row}";
    }

    private sealed record WorksheetRow(int Number, IReadOnlyList<WorksheetCell> Cells)
    {
        public string ValueAt(int column) => Cells.FirstOrDefault(cell => cell.Column == column)?.Value ?? string.Empty;
    }

    private sealed record WorksheetCell(int Column, string Value);
    private sealed record SheetMetadata(string? CategoryName, string? ListingCondition, string? PriceRange, DateTimeOffset ReportGeneratedAt);

    [GeneratedRegex(@"(?:/itm/|lineId=)(?<id>\d{9,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ListingIdRegex();

    [GeneratedRegex(@"Category:\s*(?<category>.*?)(?:,\s*Listings condition:\s*(?<condition>.*?))?(?:,\s*Price range:\s*(?<price>.*))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CategoryLineRegex();

    [GeneratedRegex(@"(?<month>\d{2})/(?<day>\d{2})/(?<year>\d{4})\s+(?<hour>\d{2})-(?<minute>\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex ReportDateRegex();
}
