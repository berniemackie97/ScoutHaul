using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Imports;

/// <summary>
/// Depop's sales export (depop.com → Settings → Sales → Download) turned into platform orders.
///
/// The export carries every figure the books need — item price, buyer shipping, the label, each of
/// Depop's fees, tax, and refunds — but no order id at all. Each order is therefore keyed from what
/// the export does carry, the same way on every import, so importing the same months twice changes
/// nothing. Refunds are applied by the refund import that already reads this file; this parser only
/// builds the orders.
/// </summary>
public static class DepopSalesReportParser
{
    public const string DateOfSale = "Date of sale";
    public const string TimeOfSale = "Time of sale";
    public const string Bundle = "Bundle";
    public const string BundleItems = "Bundle - amount of items";
    public const string Buyer = "Buyer";
    public const string Brand = "Brand";
    public const string Sku = "SKU";
    public const string Description = "Description";
    public const string Size = "Size";
    public const string ItemPrice = "Item price";
    public const string BuyerShipping = "Buyer shipping cost";
    public const string Total = "Total";
    public const string UspsCost = "USPS Cost";
    public const string DepopFee = "Depop fee";
    public const string PaymentsFee = "Depop Payments fee";
    public const string BuyerMarketplaceFee = "Buyer Marketplace Fee";
    public const string BoostingFee = "Boosting fee";
    public const string Category = "Category";
    public const string ShipName = "Name";
    public const string Address1 = "Address Line 1";
    public const string Address2 = "Address Line 2";
    public const string City = "City";
    public const string State = "State";
    public const string PostCode = "Post Code";
    public const string Country = "Country";
    public const string SalesTax = "US Sales tax";
    public const string RefundedToBuyer = "Refunded to buyer amount";
    public const string FeesRefunded = "Fees refunded to seller";

    /// <summary>The headers that identify this export among every other marketplace file.</summary>
    public static readonly string[] Signature = [DateOfSale, TimeOfSale, Buyer, Description, ItemPrice, DepopFee, "Depop Payments fee"];

    private static readonly CultureInfo UsDollars = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] DateTimeFormats =
    [
        "MM/dd/yyyy h:mm tt", "M/d/yyyy h:mm tt", "MM/dd/yyyy hh:mm tt", "M/d/yyyy hh:mm tt",
        "MM/dd/yyyy H:mm", "M/d/yyyy H:mm",
    ];

    public sealed record RowIssue(int RowNumber, string? Reference, string Message, bool Rejected);

    public sealed record ParseResult(
        IReadOnlyList<IngestPlatformOrderInput> Orders,
        IReadOnlyList<RowIssue> Issues,
        int RowsRead,
        DateTimeOffset? From,
        DateTimeOffset? To);

    public static bool Matches(string csv) => CsvTable.Load(csv, Signature) is not null;

    /// <param name="zone">The zone Depop's local dates and times are read in — the organization's.</param>
    public static ParseResult Parse(string csv, TimeZoneInfo zone)
    {
        CsvTable? table = CsvTable.Load(csv, Signature);
        if (table is null)
        {
            return new ParseResult([], [new RowIssue(0, null, "This is not a Depop sales export.", true)], 0, null, null);
        }

        List<IngestPlatformOrderInput> orders = [];
        List<RowIssue> issues = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int rowNumber = 0;

        foreach (IReadOnlyList<string> row in table.Rows)
        {
            rowNumber++;
            string description = table.Text(row, Description);
            string reference = Shorten(description);

            if (description.Length == 0)
            {
                issues.Add(new RowIssue(rowNumber, null, "No item description, so the sale cannot be identified.", true));
                continue;
            }

            if (!TrySoldAt(table, row, zone, out DateTimeOffset soldAt))
            {
                issues.Add(new RowIssue(rowNumber, reference, $"Unreadable sale date or time ('{table.Text(row, DateOfSale)} {table.Text(row, TimeOfSale)}').", true));
                continue;
            }

            Dictionary<string, decimal> money = [];
            string? unreadable = null;
            foreach (string column in new[] { ItemPrice, BuyerShipping, Total, UspsCost, DepopFee, PaymentsFee, BuyerMarketplaceFee, BoostingFee, SalesTax, RefundedToBuyer, FeesRefunded })
            {
                if (!table.TryMoney(row, column, out decimal? amount))
                {
                    unreadable = column;
                    break;
                }
                money[column] = amount ?? 0;
            }

            if (unreadable is not null)
            {
                issues.Add(new RowIssue(rowNumber, reference, $"'{unreadable}' is not a readable amount ('{table.Text(row, unreadable)}').", true));
                continue;
            }

            string buyer = table.Meaningful(row, Buyer) ?? "unknown buyer";
            string size = table.Meaningful(row, Size) ?? string.Empty;
            decimal itemPrice = money[ItemPrice];

            // A sale is who bought what, when. The same row always produces the same key, so a
            // re-import updates the order it made last time instead of adding a second one. The key
            // uses the export's own date and time text, never the converted instant: changing the
            // organization's time zone must not re-key — and so duplicate — every Depop sale.
            string printedAt = $"{table.Text(row, DateOfSale)} {table.Text(row, TimeOfSale)}";
            string orderId = "depop-" + Hash($"{printedAt}|{buyer}|{description}|{size}|{itemPrice}");
            if (!seen.Add(orderId))
            {
                issues.Add(new RowIssue(rowNumber, reference, "The same sale appears twice in this file; the second copy was ignored.", false));
                continue;
            }

            decimal sellerFees = money[DepopFee] + money[PaymentsFee] + money[BoostingFee];
            decimal label = money[UspsCost];
            decimal net = itemPrice + money[BuyerShipping] - sellerFees - label;
            string feeBreakdown = JsonSerializer.Serialize(new
            {
                depopFee = money[DepopFee],
                paymentsFee = money[PaymentsFee],
                boostingFee = money[BoostingFee],
                // Paid by the buyer to Depop — neither revenue nor a cost to the seller, kept for the record.
                buyerMarketplaceFee = money[BuyerMarketplaceFee],
                shippingLabel = label,
            });

            if (string.Equals(table.Meaningful(row, Bundle), "Yes", StringComparison.OrdinalIgnoreCase))
            {
                string count = table.Meaningful(row, BundleItems) ?? "several";
                issues.Add(new RowIssue(rowNumber, reference,
                    $"Bundle of {count} items reported as one line — check the match covers every item in it.", false));
            }

            if (money[RefundedToBuyer] > 0)
            {
                issues.Add(new RowIssue(rowNumber, reference,
                    $"Refunded {money[RefundedToBuyer].ToString("C", UsDollars)} to the buyer — applied to the sale once it is recorded.", false));
            }

            string? sku = table.Meaningful(row, Sku);
            string rawJson = RowJson(table, row);

            orders.Add(new IngestPlatformOrderInput(
                ExternalOrderId: orderId,
                OrderedAt: soldAt,
                ItemsSubtotal: itemPrice,
                ShippingCharged: money[BuyerShipping],
                TaxCollected: money[SalesTax],
                FeeTotal: sellerFees,
                Lines:
                [
                    new IngestPlatformOrderLineInput(
                        Title: description,
                        UnitPrice: itemPrice,
                        Quantity: 1,
                        // Stable per item, so a match made against this line survives re-imports.
                        ExternalItemId: "depop-line-" + Hash($"{description}|{size}|{itemPrice}"),
                        Sku: sku,
                        ShippingCharged: money[BuyerShipping],
                        TaxCollected: money[SalesTax],
                        PlatformFees: sellerFees,
                        SellerShippingCost: label,
                        NetSales: net,
                        TotalSales: money[Total],
                        FeeBreakdownJson: feeBreakdown,
                        RawJson: rawJson),
                ],
                Platform: "Depop",
                ShippingCost: label,
                Currency: "USD",
                // Depop collects and remits US sales tax as the marketplace facilitator.
                TaxRemittedByPlatform: true,
                BuyerReference: buyer,
                ShipToName: table.Meaningful(row, ShipName),
                ShipToLine1: table.Meaningful(row, Address1),
                ShipToLine2: table.Meaningful(row, Address2),
                ShipToCity: table.Meaningful(row, City),
                ShipToRegion: table.Meaningful(row, State),
                ShipToPostalCode: table.Meaningful(row, PostCode),
                ShipToCountry: table.Meaningful(row, Country),
                PaidAt: soldAt,
                FeeBreakdownJson: feeBreakdown,
                RawJson: rawJson,
                // The report has no order ids, so these keys give way to Depop's own when a scan reads the same sale.
                KeysAreDerived: true));
        }

        DateTimeOffset? from = orders.Count == 0 ? null : orders.Min(order => order.OrderedAt);
        DateTimeOffset? to = orders.Count == 0 ? null : orders.Max(order => order.OrderedAt);
        return new ParseResult(orders, issues, rowNumber, from, to);
    }

    private static bool TrySoldAt(CsvTable table, IReadOnlyList<string> row, TimeZoneInfo zone, out DateTimeOffset soldAt)
    {
        soldAt = default;
        string text = $"{table.Text(row, DateOfSale)} {table.Text(row, TimeOfSale)}".Trim();
        if (!DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return false;
        }

        // Depop prints the seller's local time with no offset; read it in the business's own zone.
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        soldAt = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
        return true;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()))).ToLowerInvariant()[..20];

    private static string Shorten(string description) =>
        description.Length <= 60 ? description : description[..57] + "…";

    private static string RowJson(CsvTable table, IReadOnlyList<string> row) =>
        JsonSerializer.Serialize(table.Header
            .Select((header, index) => (header, value: index < row.Count ? row[index] : string.Empty))
            .DistinctBy(pair => pair.header)
            .ToDictionary(pair => pair.header, pair => pair.value));
}
