using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HaulScout.Application.Contracts;
using HaulScout.Application.Imports;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Application.Sync;

/// <summary>What a report is about, which decides who may import it and what it changes.</summary>
public enum ReportArea
{
    /// <summary>Sales, fees and refunds — the books.</summary>
    Orders,

    /// <summary>What is listed where, at what price.</summary>
    Listings,

    /// <summary>Listing-performance observations; reporting input, never matched.</summary>
    ListingQuality,
}

/// <summary>A marketplace export this system knows how to read.</summary>
/// <param name="Key">Stable identifier recorded on every run, e.g. <c>depop.sales</c>.</param>
/// <param name="CompleteSnapshot">
/// True when the export lists everything of its kind as of a date — Poshmark's available listings —
/// so what it no longer contains is no longer live.
/// </param>
public sealed record ReportFormat(
    string Key,
    string Platform,
    string Label,
    ReportArea Area,
    string RequiredPermission,
    bool CompleteSnapshot = false);

/// <summary>A dropped file: its name and exact bytes.</summary>
public sealed record ReportFile(string FileName, byte[] Content)
{
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Content)).ToLowerInvariant();

    /// <summary>The file as text, honouring a byte-order mark; exports are UTF-8 unless they say otherwise.</summary>
    public string Text()
    {
        using StreamReader reader = new(new MemoryStream(Content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public bool LooksLikeWorkbook =>
        Content.Length > 4 && Content[0] == 0x50 && Content[1] == 0x4B && Content[2] == 0x03 && Content[3] == 0x04;

    public byte[] Compressed()
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Content);
        }
        return output.ToArray();
    }

    public static byte[] Decompress(byte[] compressed)
    {
        using GZipStream gzip = new(new MemoryStream(compressed), CompressionMode.Decompress);
        using MemoryStream output = new();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}

/// <summary>
/// Every export the importer recognises, and how each is recognised — by its content, never by its
/// file name and never by which button the operator pressed.
///
/// Before this the operator chose one of four upload cards and had to know which eBay export went
/// where; a file in the wrong card failed half-way through, or worse, half-imported.
/// </summary>
public static class ReportFormats
{
    public static readonly ReportFormat EbayAllOrders = new(
        "ebay.all-orders", "eBay", "eBay All Orders report", ReportArea.Orders, HaulScoutPermissions.ManageFinance);

    public static readonly ReportFormat EbayListingSales = new(
        "ebay.listing-sales", "eBay", "eBay Listings Sales report (fees and refunds)", ReportArea.Orders, HaulScoutPermissions.ManageFinance);

    public static readonly ReportFormat EbayActiveListings = new(
        "ebay.active-listings", "eBay", "eBay Active Listings report", ReportArea.Listings, HaulScoutPermissions.ManageListings);

    public static readonly ReportFormat EbayListingQuality = new(
        "ebay.listing-quality", "eBay", "eBay Listing Quality workbook", ReportArea.ListingQuality, HaulScoutPermissions.ManageListings);

    public static readonly ReportFormat DepopSales = new(
        "depop.sales", "Depop", "Depop sales export", ReportArea.Orders, HaulScoutPermissions.ManageFinance);

    public static readonly ReportFormat PoshmarkInventory = new(
        "poshmark.inventory", "Poshmark", "Poshmark inventory report", ReportArea.Listings, HaulScoutPermissions.ManageListings,
        CompleteSnapshot: true);

    public static readonly IReadOnlyList<ReportFormat> All =
        [EbayAllOrders, EbayListingSales, EbayActiveListings, EbayListingQuality, DepopSales, PoshmarkInventory];

    /// <summary>Recorded for a file nothing recognises, so the attempt is in the history too.</summary>
    public const string UnrecognisedKey = "unrecognised";

    public static ReportFormat? Find(string key) => All.FirstOrDefault(format => format.Key == key);

    /// <summary>Which export this file is, or null. Each format's own parser decides — one definition each.</summary>
    public static ReportFormat? Detect(ReportFile file)
    {
        if (file.LooksLikeWorkbook)
        {
            return EbayListingQualityWorkbookParser.Parse(file.Content, DateTimeOffset.UtcNow).Rows.Count > 0 ? EbayListingQuality : null;
        }

        string text = file.Text();
        return EbayReportParser.DetectKind(text) switch
        {
            EbayReportParser.EbayReportKind.AllOrders => EbayAllOrders,
            EbayReportParser.EbayReportKind.ListingSales => EbayListingSales,
            _ when EbayActiveListingsParser.Matches(text) => EbayActiveListings,
            _ when DepopSalesReportParser.Matches(text) => DepopSales,
            _ when PoshmarkInventoryReportParser.Matches(text) => PoshmarkInventory,
            _ => null,
        };
    }
}

/// <summary>A problem with one row of a file, in the words shown to the operator.</summary>
public sealed record ReportRowIssue(int RowNumber, string? Reference, string Message, bool Rejected);

/// <summary>
/// A file, recognised and read, before anything is written: what it holds and what it would do.
/// The same read feeds the import, so the preview and the import can never disagree.
/// </summary>
public sealed record ReadReport(
    ReportFormat Format,
    int Records,
    DateTimeOffset? CoversFrom,
    DateTimeOffset? CoversTo,
    IReadOnlyList<ReportRowIssue> Issues,
    IReadOnlyList<IngestPlatformOrderInput> Orders,
    IReadOnlyList<EbayReportParser.EbayListingSalesMetric> ListingSales,
    IReadOnlyList<IngestPlatformListingInput> Listings);

public static class ReportReader
{
    /// <param name="zone">The organization's zone, for exports that print local times without an offset.</param>
    public static ReadReport Read(ReportFormat format, ReportFile file, TimeZoneInfo zone)
    {
        if (format == ReportFormats.EbayListingQuality)
        {
            EbayListingQualityWorkbookParser.ParseResult quality = EbayListingQualityWorkbookParser.Parse(file.Content, DateTimeOffset.UtcNow);
            return Empty(format, quality.Rows.Count, quality.Warnings.Select(warning => new ReportRowIssue(0, null, warning, false)).ToList());
        }

        string text = file.Text();

        if (format == ReportFormats.EbayAllOrders || format == ReportFormats.EbayListingSales)
        {
            EbayReportParser.ParseResult parsed = EbayReportParser.ParseReport(text);
            List<ReportRowIssue> issues = parsed.Warning is null ? [] : [new ReportRowIssue(0, null, parsed.Warning, false)];
            return format == ReportFormats.EbayAllOrders
                ? new ReadReport(format, parsed.Orders.Count,
                    parsed.Orders.Count == 0 ? null : parsed.Orders.Min(order => order.OrderedAt),
                    parsed.Orders.Count == 0 ? null : parsed.Orders.Max(order => order.OrderedAt),
                    issues, parsed.Orders, [], [])
                : new ReadReport(format, parsed.ListingSales.Count, null, null, issues, [], parsed.ListingSales, []);
        }

        if (format == ReportFormats.EbayActiveListings)
        {
            EbayActiveListingsParser.ParseResult parsed = EbayActiveListingsParser.Parse(text);
            List<ReportRowIssue> issues = parsed.Warning is null ? [] : [new ReportRowIssue(0, null, parsed.Warning, false)];
            return new ReadReport(format, parsed.Listings.Count, null, null, issues, [], [], parsed.Listings);
        }

        if (format == ReportFormats.DepopSales)
        {
            DepopSalesReportParser.ParseResult parsed = DepopSalesReportParser.Parse(text, zone);
            return new ReadReport(format, parsed.Orders.Count, parsed.From, parsed.To,
                parsed.Issues.Select(issue => new ReportRowIssue(issue.RowNumber, issue.Reference, issue.Message, issue.Rejected)).ToList(),
                parsed.Orders, [], []);
        }

        if (format == ReportFormats.PoshmarkInventory)
        {
            PoshmarkInventoryReportParser.ParseResult parsed = PoshmarkInventoryReportParser.Parse(text, zone);
            return new ReadReport(format, parsed.Listings.Count, null, parsed.AsOf,
                parsed.Issues.Select(issue => new ReportRowIssue(issue.RowNumber, issue.Reference, issue.Message, issue.Rejected)).ToList(),
                [], [], parsed.Listings);
        }

        throw new ArgumentOutOfRangeException(nameof(format), format.Key, "No reader for this format.");
    }

    private static ReadReport Empty(ReportFormat format, int records, IReadOnlyList<ReportRowIssue> issues) =>
        new(format, records, null, null, issues, [], [], []);
}
