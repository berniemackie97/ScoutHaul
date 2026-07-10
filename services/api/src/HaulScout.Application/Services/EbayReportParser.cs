using System.Globalization;
using System.Text;
using System.Text.Json;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

/// <summary>
/// Parses eBay's Seller Hub "Sales / Listings" report CSV — the authoritative fee source — into normalized
/// ingest orders. The report is per listing sold in a date range: each row already carries eBay's exact fee
/// stack (final value, promoted, international, credits) plus the shipping-label cost, keyed by eBay item id.
/// It has no per-sale date or order id, so we key each row by its item id and date it to the report's end date;
/// reconciliation preserves an existing sale's real date and only overwrites the money. Columns are mapped by
/// HEADER NAME (not position) so eBay reordering a column can't silently corrupt the numbers.
/// </summary>
public static class EbayReportParser
{
    public sealed record ParseResult(IReadOnlyList<IngestPlatformOrderInput> Orders, int RowsParsed, int RowsSkipped, string? Warning);

    private const string ItemIdHeader = "eBay item ID";
    private const string TitleHeader = "Listing title";

    public static ParseResult ParseSalesReport(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new ParseResult([], 0, 0, "The file was empty.");
        }

        List<List<string>> rows = SplitRecords(csv).Select(ParseCsvLine).ToList();
        int headerIndex = rows.FindIndex(fields => fields.Contains(TitleHeader) && fields.Contains(ItemIdHeader));
        if (headerIndex < 0)
        {
            return new ParseResult([], 0, 0, "This does not look like an eBay sales report (no 'eBay item ID' header row).");
        }

        List<string> header = rows[headerIndex];
        Dictionary<string, int> column = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            column.TryAdd(header[i].Trim(), i);
        }

        DateTimeOffset orderedAt = ExtractReportEndDate(rows, headerIndex) ?? DateTimeOffset.UtcNow;

        List<IngestPlatformOrderInput> orders = [];
        int skipped = 0;
        for (int i = headerIndex + 1; i < rows.Count; i++)
        {
            List<string> fields = rows[i];
            string itemId = Field(fields, column, ItemIdHeader).Trim();
            string title = Field(fields, column, TitleHeader).Trim();
            if (itemId.Length == 0 || title.Length == 0)
            {
                continue; // blank/trailer line, not a data row
            }

            decimal itemSales = Money(Field(fields, column, "Item sales"));
            decimal totalSales = Money(Field(fields, column, "Total sales (Includes taxes)"));
            if (itemSales <= 0 && totalSales <= 0)
            {
                skipped++; // a $0 report artifact (e.g. a fee-only adjustment row) — nothing to reconcile
                continue;
            }

            decimal taxToYou = Money(Field(fields, column, "Taxes and government fees paid by buyer to you"));
            decimal taxToEbay = Money(Field(fields, column, "Taxes and government fees paid by buyer to eBay"));
            decimal shipToBuyer = Money(Field(fields, column, "Shipping and handling paid by buyer to you"));
            decimal totalSellingCosts = Money(FieldStartsWith(fields, column, "Total selling costs"));
            decimal shippingLabel = Money(FieldStartsWith(fields, column, "Shipping labels cost"));
            int quantity = (int)Math.Max(1, Money(Field(fields, column, "Quantity sold")));

            // eBay's own cut = every selling cost EXCEPT the shipping label the seller bought (that's a seller cost,
            // tracked separately). Identity holds on the report: sellingCosts = fees − credits + label.
            decimal platformFees = Math.Max(0, totalSellingCosts - shippingLabel);

            orders.Add(new IngestPlatformOrderInput(
                ExternalOrderId: itemId,
                OrderedAt: orderedAt,
                ItemsSubtotal: itemSales,
                ShippingCharged: shipToBuyer,
                TaxCollected: taxToYou + taxToEbay,
                FeeTotal: platformFees,
                Lines: [new IngestPlatformOrderLineInput(
                    Title: title,
                    UnitPrice: quantity > 0 ? decimal.Round(itemSales / quantity, 2, MidpointRounding.AwayFromZero) : itemSales,
                    Quantity: quantity,
                    ExternalItemId: itemId)],
                Platform: "eBay",
                ShippingCost: shippingLabel > 0 ? shippingLabel : null,
                PayoutAmount: null,
                Currency: "USD",
                // eBay remits the tax it collected ("to eBay"); tax "to you" is seller-collected and owed.
                TaxRemittedByPlatform: taxToYou == 0,
                RawJson: JsonSerializer.Serialize(fields)));
        }

        string? warning = orders.Count == 0
            ? "No sold-listing rows were found in the report."
            : skipped > 0 ? $"{skipped} zero-value row(s) skipped." : null;
        return new ParseResult(orders, orders.Count, skipped, warning);
    }

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

    private static DateTimeOffset? ExtractReportEndDate(IReadOnlyList<IReadOnlyList<string>> rows, int headerIndex)
    {
        for (int i = 0; i < headerIndex; i++)
        {
            foreach (string cell in rows[i])
            {
                int to = cell.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
                if (to > 0 && cell.Contains("Report", StringComparison.OrdinalIgnoreCase))
                {
                    string tail = cell[(to + 4)..].Trim().TrimEnd('"');
                    if (DateTimeOffset.TryParse(tail, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
                    {
                        return parsed;
                    }
                }
            }
        }
        return null;
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
