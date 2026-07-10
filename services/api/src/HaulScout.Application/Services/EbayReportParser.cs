using System.Globalization;
using System.Text;
using System.Text.Json;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

/// <summary>
/// Parses eBay seller CSV reports by header name. The all-orders report is the authoritative source for order
/// identity, buyer, item lines, tax, shipping, and tracking. The listing-sales report is an enrichment source for
/// line-level selling costs/net sales; it is not an order source because it is keyed by listing/item id.
/// </summary>
public static class EbayReportParser
{
    public enum EbayReportKind
    {
        Unknown,
        AllOrders,
        ListingSales
    }

    public sealed record ParseResult(
        EbayReportKind Kind,
        IReadOnlyList<IngestPlatformOrderInput> Orders,
        IReadOnlyList<EbayListingSalesMetric> ListingSales,
        int RowsParsed,
        int RowsSkipped,
        string? Warning);

    public sealed record EbayListingSalesMetric(
        string ExternalItemId,
        string Title,
        int QuantitySold,
        decimal TotalSales,
        decimal ItemSales,
        decimal TaxCollected,
        decimal ShippingCharged,
        decimal PlatformFees,
        decimal SellerShippingCost,
        decimal NetSales,
        decimal AverageSellingPrice,
        int QuantitySoldViaPromotedListing,
        int QuantitySoldViaBestOffers,
        int QuantitySoldViaSellerInitiatedOffers,
        string FeeBreakdownJson,
        string RawJson);

    private const string ListingSalesItemIdHeader = "eBay item ID";
    private const string ListingSalesTitleHeader = "Listing title";
    private const string AllOrdersOrderNumberHeader = "Order Number";
    private const string AllOrdersItemNumberHeader = "Item Number";

    public static ParseResult ParseReport(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new ParseResult(EbayReportKind.Unknown, [], [], 0, 0, "The file was empty.");
        }

        List<List<string>> rows = SplitRecords(csv).Select(ParseCsvLine).ToList();
        int allOrdersHeader = rows.FindIndex(fields => fields.Contains(AllOrdersOrderNumberHeader) && fields.Contains(AllOrdersItemNumberHeader));
        if (allOrdersHeader >= 0)
        {
            return ParseAllOrdersReport(rows, allOrdersHeader);
        }

        int listingSalesHeader = rows.FindIndex(fields => fields.Contains(ListingSalesTitleHeader) && fields.Contains(ListingSalesItemIdHeader));
        if (listingSalesHeader >= 0)
        {
            return ParseListingSalesReport(rows, listingSalesHeader);
        }

        return new ParseResult(
            EbayReportKind.Unknown,
            [],
            [],
            0,
            0,
            "This does not look like a supported eBay report. Use the all-orders CSV for order reconciliation, or the listing-sales CSV for fee enrichment.");
    }

    // Back-compatible entry point used by older tests/callers.
    public static ParseResult ParseSalesReport(string csv) => ParseReport(csv);

    private static ParseResult ParseAllOrdersReport(IReadOnlyList<List<string>> rows, int headerIndex)
    {
        Dictionary<string, int> column = BuildColumnMap(rows[headerIndex]);
        List<Dictionary<string, string>> dataRows = [];
        for (int i = headerIndex + 1; i < rows.Count; i++)
        {
            List<string> row = rows[i];
            if (string.IsNullOrWhiteSpace(Field(row, column, "Order Number")))
            {
                continue;
            }
            dataRows.Add(Materialize(row, column));
        }

        List<IngestPlatformOrderInput> orders = [];
        int skipped = 0;
        foreach (IGrouping<string, Dictionary<string, string>> group in dataRows.GroupBy(row => row["Order Number"].Trim()))
        {
            List<Dictionary<string, string>> rowsForOrder = group.ToList();
            List<Dictionary<string, string>> itemRows = rowsForOrder.Where(row => !string.IsNullOrWhiteSpace(row.GetValueOrDefault("Item Number"))).ToList();
            if (itemRows.Count == 0)
            {
                skipped++;
                continue;
            }

            Dictionary<string, string>? summaryRow = rowsForOrder.FirstOrDefault(row => string.IsNullOrWhiteSpace(row.GetValueOrDefault("Item Number")));
            Dictionary<string, string> orderRow = summaryRow ?? itemRows[0];
            DateTimeOffset orderedAt = Date(Field(orderRow, "Sale Date")) ?? Date(Field(orderRow, "Paid On Date")) ?? DateTimeOffset.UtcNow;
            DateTimeOffset? paidAt = Date(Field(orderRow, "Paid On Date"));
            DateTimeOffset? shippedAt = Date(FirstNonBlank(rowsForOrder, "Shipped On Date"));

            List<IngestPlatformOrderLineInput> lines = [];
            foreach (Dictionary<string, string> itemRow in itemRows)
            {
                decimal soldFor = Money(Field(itemRow, "Sold For"));
                int quantity = (int)Math.Max(1, Money(Field(itemRow, "Quantity")));
                lines.Add(new IngestPlatformOrderLineInput(
                    Title: TrimToDefault(Field(itemRow, "Item Title"), "Untitled eBay item"),
                    UnitPrice: quantity > 0 ? decimal.Round(soldFor / quantity, 2, MidpointRounding.AwayFromZero) : soldFor,
                    Quantity: quantity,
                    ExternalItemId: TrimToNull(Field(itemRow, "Item Number")),
                    Sku: TrimToNull(Field(itemRow, "Custom Label")),
                    ShippingCharged: Money(Field(itemRow, "Shipping And Handling")),
                    TaxCollected: Money(Field(itemRow, "Seller Collected Tax")) + Money(Field(itemRow, "eBay Collected Tax")),
                    TotalSales: Money(Field(itemRow, "Total Price")),
                    RawJson: JsonSerializer.Serialize(itemRow)));
            }

            decimal itemSubtotal = itemRows.Sum(row => Money(Field(row, "Sold For")));
            decimal orderSubtotal = Money(Field(orderRow, "Sold For"));
            if (orderSubtotal > itemSubtotal)
            {
                itemSubtotal = orderSubtotal;
            }

            decimal shipping = summaryRow is not null
                ? Money(Field(summaryRow, "Shipping And Handling"))
                : itemRows.Sum(row => Money(Field(row, "Shipping And Handling")));
            decimal sellerTax = summaryRow is not null
                ? Money(Field(summaryRow, "Seller Collected Tax"))
                : itemRows.Sum(row => Money(Field(row, "Seller Collected Tax")));
            decimal ebayTax = summaryRow is not null
                ? Money(Field(summaryRow, "eBay Collected Tax"))
                : itemRows.Sum(row => Money(Field(row, "eBay Collected Tax")));
            string? tracking = TrimToNull(FirstNonBlank(rowsForOrder, "Tracking Number"));
            string? carrier = TrimToNull(FirstNonBlank(rowsForOrder, "Shipping Service"));

            orders.Add(new IngestPlatformOrderInput(
                ExternalOrderId: group.Key,
                OrderedAt: orderedAt,
                ItemsSubtotal: itemSubtotal,
                ShippingCharged: shipping,
                TaxCollected: sellerTax + ebayTax,
                FeeTotal: 0,
                Lines: lines,
                Platform: "eBay",
                ShippingCost: null,
                PayoutAmount: null,
                Currency: "USD",
                TaxRemittedByPlatform: sellerTax == 0,
                BuyerName: TrimToNull(Field(orderRow, "Buyer Name")),
                BuyerReference: TrimToNull(Field(orderRow, "Buyer Username")),
                BuyerEmail: TrimToNull(Field(orderRow, "Buyer Email")),
                BuyerNote: TrimToNull(Field(orderRow, "Buyer Note")),
                ShipToName: TrimToNull(Field(orderRow, "Ship To Name")),
                ShipToPhone: TrimToNull(Field(orderRow, "Ship To Phone")),
                ShipToLine1: TrimToNull(Field(orderRow, "Ship To Address 1")),
                ShipToLine2: TrimToNull(Field(orderRow, "Ship To Address 2")),
                ShipToCity: TrimToNull(Field(orderRow, "Ship To City")),
                ShipToRegion: TrimToNull(Field(orderRow, "Ship To State")),
                ShipToPostalCode: TrimToNull(Field(orderRow, "Ship To Zip")),
                ShipToCountry: TrimToNull(Field(orderRow, "Ship To Country")),
                Carrier: carrier,
                TrackingNumber: tracking,
                PaidAt: paidAt,
                ShippedAt: shippedAt,
                RawJson: JsonSerializer.Serialize(rowsForOrder)));
        }

        string? warning = orders.Count == 0
            ? "No order rows were found in the eBay all-orders report."
            : skipped > 0 ? $"{skipped} order group(s) skipped because they contained no item rows." : null;
        return new ParseResult(EbayReportKind.AllOrders, orders, [], orders.Count, skipped, warning);
    }

    private static ParseResult ParseListingSalesReport(IReadOnlyList<List<string>> rows, int headerIndex)
    {
        Dictionary<string, int> column = BuildColumnMap(rows[headerIndex]);
        List<EbayListingSalesMetric> metrics = [];
        int skipped = 0;
        for (int i = headerIndex + 1; i < rows.Count; i++)
        {
            List<string> fields = rows[i];
            string itemId = Field(fields, column, ListingSalesItemIdHeader).Trim();
            string title = Field(fields, column, ListingSalesTitleHeader).Trim();
            if (itemId.Length == 0 || title.Length == 0)
            {
                continue;
            }

            decimal itemSales = Money(Field(fields, column, "Item sales"));
            decimal totalSales = Money(Field(fields, column, "Total sales (Includes taxes)"));
            if (itemSales <= 0 && totalSales <= 0)
            {
                skipped++;
                continue;
            }

            decimal taxToYou = Money(Field(fields, column, "Taxes and government fees paid by buyer to you"));
            decimal taxToEbay = Money(Field(fields, column, "Taxes and government fees paid by buyer to eBay"));
            decimal totalSellingCosts = Money(FieldStartsWith(fields, column, "Total selling costs"));
            decimal shippingLabel = Money(FieldStartsWith(fields, column, "Shipping labels cost"));
            decimal platformFees = Math.Max(0, totalSellingCosts - shippingLabel);

            var feeBreakdown = new SortedDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (string header in new[]
            {
                "Insertion fees",
                "Optional listing upgrade fees",
                "Final value fees",
                "Promoted Listings - General fees",
                "Promoted Listings - Priority fees",
                "Ads Express fees",
                "Promoted Offsite - Fees",
                "International fees",
                "Other eBay fees",
                "Deposit processing fees",
                "Fee credits"
            })
            {
                feeBreakdown[header] = Money(Field(fields, column, header));
            }

            metrics.Add(new EbayListingSalesMetric(
                ExternalItemId: itemId,
                Title: title,
                QuantitySold: (int)Math.Max(1, Money(Field(fields, column, "Quantity sold"))),
                TotalSales: totalSales,
                ItemSales: itemSales,
                TaxCollected: taxToYou + taxToEbay,
                ShippingCharged: Money(Field(fields, column, "Shipping and handling paid by buyer to you")),
                PlatformFees: platformFees,
                SellerShippingCost: shippingLabel,
                NetSales: Money(FieldStartsWith(fields, column, "Net sales")),
                AverageSellingPrice: Money(Field(fields, column, "Average Selling price")),
                QuantitySoldViaPromotedListing: (int)Money(Field(fields, column, "Quantity sold via promoted listing")),
                QuantitySoldViaBestOffers: (int)Money(Field(fields, column, "Quantity sold via Best Offers")),
                QuantitySoldViaSellerInitiatedOffers: (int)Money(Field(fields, column, "Quantity sold via Seller Initiated Offers")),
                FeeBreakdownJson: JsonSerializer.Serialize(feeBreakdown),
                RawJson: JsonSerializer.Serialize(fields)));
        }

        string? warning = metrics.Count == 0
            ? "No listing-sales rows were found in the eBay report."
            : skipped > 0 ? $"{skipped} zero-value row(s) skipped." : null;
        return new ParseResult(EbayReportKind.ListingSales, [], metrics, metrics.Count, skipped, warning);
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> header)
    {
        Dictionary<string, int> column = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            column.TryAdd(header[i].Trim(), i);
        }
        return column;
    }

    private static Dictionary<string, string> Materialize(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> column) =>
        column.ToDictionary(pair => pair.Key, pair => pair.Value < row.Count ? row[pair.Value] : string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string Field(IReadOnlyDictionary<string, string> row, string name) =>
        row.TryGetValue(name, out string? value) ? value : string.Empty;

    private static string Field(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> column, string name) =>
        column.TryGetValue(name, out int index) && index < fields.Count ? fields[index] : string.Empty;

    // eBay appends parentheticals to some headers ("Shipping labels cost (Amount you paid...)"); match by prefix.
    private static string FieldStartsWith(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> column, string prefix)
    {
        foreach ((string key, int index) in column)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && index < fields.Count)
            {
                return fields[index];
            }
        }
        return string.Empty;
    }

    private static string FirstNonBlank(IEnumerable<IReadOnlyDictionary<string, string>> rows, string name) =>
        rows.Select(row => Field(row, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    /// <summary>Parses eBay money: "$1,234.56", "($12.34)" (negative), "-", "" → decimal.</summary>
    private static decimal Money(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }
        string text = raw.Trim();
        bool negative = text.StartsWith('(') && text.EndsWith(')');
        text = text.Replace("(", "").Replace(")", "").Replace("$", "").Replace(",", "").Trim();
        if (text.Length == 0 || text == "-")
        {
            return 0;
        }
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return 0;
        }
        return negative ? -value : value;
    }

    private static DateTimeOffset? Date(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (string format in new[] { "MMM-dd-yy", "MMM-d-yy", "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" })
        {
            if (DateTimeOffset.TryParseExact(raw.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset fallback) ? fallback : null;
    }

    private static string TrimToDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? TrimToNull(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    // Records are newline-separated, but a quoted field may contain newlines — split with quote awareness.
    private static IEnumerable<string> SplitRecords(string csv)
    {
        StringBuilder current = new();
        bool inQuotes = false;
        foreach (char c in csv.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if (c == '\n' && !inQuotes)
            {
                yield return current.ToString();
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        List<string> fields = [];
        StringBuilder current = new();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
