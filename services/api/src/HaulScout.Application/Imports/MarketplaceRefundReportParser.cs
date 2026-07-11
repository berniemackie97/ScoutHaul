using System.Text.Json;

namespace HaulScout.Application.Imports;

/// <summary>
/// Parses marketplace refund evidence from seller exports. This parser only extracts refund signals; database
/// reconciliation stays in the API layer where exact sale/order matches can be enforced.
/// </summary>
public static class MarketplaceRefundReportParser
{
    public enum MarketplaceRefundReportKind
    {
        Unknown,
        EbayListingSales,
        DepopSales
    }

    public sealed record ParseResult(
        MarketplaceRefundReportKind Kind,
        IReadOnlyList<MarketplaceRefundEvent> Events,
        int RowsParsed,
        int RowsSkipped,
        string? Warning);

    public sealed record MarketplaceRefundEvent(
        string Platform,
        string Source,
        string? ExternalItemId,
        string? Sku,
        string Title,
        DateTimeOffset? SoldAt,
        decimal ItemPrice,
        decimal BuyerRefundTotal,
        decimal FeeCredit,
        bool FullRefundInferred,
        string RawJson);

    private const string EbayTitle = "Listing title";
    private const string EbayItemId = "eBay item ID";
    private const string EbayItemSales = "Item sales";
    private const string EbayTotalSales = "Total sales (Includes taxes)";
    private const string EbayFeeCredits = "Fee credits";

    private const string DepopDateOfSale = "Date of sale";
    private const string DepopDescription = "Description";
    private const string DepopSku = "SKU";
    private const string DepopItemPrice = "Item price";
    private const string DepopBuyerRefund = "Refunded to buyer amount";
    private const string DepopFeeRefund = "Fees refunded to seller";

    public static ParseResult Parse(string? platform, string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new ParseResult(MarketplaceRefundReportKind.Unknown, [], 0, 0, "The file was empty.");
        }

        CsvTable? depop = CsvTable.Load(csv, DepopDateOfSale, DepopDescription, DepopBuyerRefund, DepopFeeRefund);
        if (depop is not null)
        {
            return ParseDepop(depop);
        }

        CsvTable? ebay = CsvTable.Load(csv, EbayTitle, EbayItemId, EbayItemSales, EbayTotalSales, EbayFeeCredits);
        if (ebay is not null)
        {
            return ParseEbayListingSales(ebay);
        }

        return new ParseResult(
            MarketplaceRefundReportKind.Unknown,
            [],
            0,
            0,
            "This does not look like a supported refund export. Use Depop Sales or eBay Listing Sales reports.");
    }

    private static ParseResult ParseEbayListingSales(CsvTable table)
    {
        List<MarketplaceRefundEvent> events = [];
        int skipped = 0;
        foreach (IReadOnlyList<string> row in table.Rows)
        {
            string itemId = table.Text(row, EbayItemId);
            string title = table.Text(row, EbayTitle);
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(title))
            {
                skipped++;
                continue;
            }

            decimal itemSales = table.Money(row, EbayItemSales);
            decimal totalSales = table.Money(row, EbayTotalSales);
            decimal feeCredit = table.Money(row, EbayFeeCredits);
            decimal netSales = table.MoneyStartsWith(row, "Net sales");
            bool refundSignal = feeCredit > 0 && itemSales <= 0 && totalSales <= 0 && netSales < 0;
            if (!refundSignal)
            {
                skipped++;
                continue;
            }

            events.Add(new MarketplaceRefundEvent(
                Platform: "eBay",
                Source: "eBay Listing Sales",
                ExternalItemId: itemId.Trim(),
                Sku: null,
                Title: title.Trim(),
                SoldAt: null,
                ItemPrice: 0,
                BuyerRefundTotal: 0,
                FeeCredit: feeCredit,
                FullRefundInferred: true,
                RawJson: RowJson(table, row)));
        }

        string? warning = events.Count == 0
            ? "No refund-like eBay listing-sales rows were found."
            : skipped > 0 ? $"{skipped} non-refund row(s) skipped." : null;
        return new ParseResult(MarketplaceRefundReportKind.EbayListingSales, events, events.Count, skipped, warning);
    }

    private static ParseResult ParseDepop(CsvTable table)
    {
        List<MarketplaceRefundEvent> events = [];
        int skipped = 0;
        foreach (IReadOnlyList<string> row in table.Rows)
        {
            string title = table.Text(row, DepopDescription);
            decimal buyerRefund = table.Money(row, DepopBuyerRefund);
            decimal feeCredit = table.Money(row, DepopFeeRefund);
            if (string.IsNullOrWhiteSpace(title) || (buyerRefund <= 0 && feeCredit <= 0))
            {
                skipped++;
                continue;
            }

            string? sku = NormalizeNullable(table.Text(row, DepopSku));
            events.Add(new MarketplaceRefundEvent(
                Platform: "Depop",
                Source: "Depop Sales",
                ExternalItemId: null,
                Sku: string.Equals(sku, "N/A", StringComparison.OrdinalIgnoreCase) ? null : sku,
                Title: title.Trim(),
                SoldAt: table.Date(row, DepopDateOfSale),
                ItemPrice: table.Money(row, DepopItemPrice),
                BuyerRefundTotal: buyerRefund,
                FeeCredit: feeCredit,
                FullRefundInferred: false,
                RawJson: RowJson(table, row)));
        }

        string? warning = events.Count == 0
            ? "No Depop refund rows were found."
            : skipped > 0 ? $"{skipped} non-refund row(s) skipped." : null;
        return new ParseResult(MarketplaceRefundReportKind.DepopSales, events, events.Count, skipped, warning);
    }

    private static string RowJson(CsvTable table, IReadOnlyList<string> row)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < table.Header.Count; i++)
        {
            values[table.Header[i]] = i < row.Count ? row[i] : string.Empty;
        }
        return JsonSerializer.Serialize(values);
    }

    private static string? NormalizeNullable(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
