using System.Text.Json;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Imports;

/// <summary>
/// Parses eBay's Seller Hub "All Orders" report — the authoritative ORDER source.
///
/// It carries the real order number, the buyer (username, name, email), the ship-to address, tracking, and the
/// per-item lines. Rows are grouped by <c>Order Number</c>, so a multi-item order becomes ONE order with several
/// lines instead of several fake orders.
///
/// What it does NOT carry is fees. This parser therefore reports <c>FeeTotal = 0</c> and never pretends otherwise;
/// fees arrive from the Sales report, matched by item number. The Sales report must never create orders — it has
/// no order number, and keying orders by item number is precisely how orphaned, buyer-less orders were created.
/// </summary>
public static class EbayOrdersReportParser
{
    public const string OrderNumber = "Order Number";
    public const string ItemNumber = "Item Number";
    public const string BuyerUsername = "Buyer Username";

    public sealed record ParseResult(
        IReadOnlyList<IngestPlatformOrderInput> Orders,
        int RowsParsed,
        int RowsSkipped,
        string? Warning);

    /// <summary>Header signature used to detect this report among the others.</summary>
    public static bool Matches(string csv) =>
        CsvTable.Load(csv, OrderNumber, ItemNumber, BuyerUsername, "Sold For") is not null;

    public static ParseResult Parse(string csv)
    {
        CsvTable? table = CsvTable.Load(csv, OrderNumber, ItemNumber, BuyerUsername, "Sold For");
        if (table is null)
        {
            return new ParseResult([], 0, 0, "This does not look like an eBay All Orders report.");
        }

        var groups = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
        int skipped = 0;
        foreach (var row in table.Rows)
        {
            string orderNumber = table.Text(row, OrderNumber);
            bool hasItem = table.Text(row, ItemNumber).Length > 0;
            bool hasMoney = table.Text(row, "Sold For").Length > 0;
            bool hasBuyer = table.Text(row, BuyerUsername).Length > 0;

            // eBay appends a trailer ("N record(s) downloaded") that lands in the Order Number column.
            if (orderNumber.Length == 0 || (!hasItem && !hasMoney && !hasBuyer))
            {
                skipped++;
                continue;
            }
            if (!groups.TryGetValue(orderNumber, out var rows))
            {
                groups[orderNumber] = rows = [];
            }
            rows.Add(row);
        }

        var orders = new List<IngestPlatformOrderInput>();
        foreach ((string orderNumber, List<IReadOnlyList<string>> rows) in groups)
        {
            // A multi-item order is header/detail: one SUMMARY row without an item number that carries the buyer,
            // ship-to, shipping, tax and totals — and N ITEM rows that carry the item, its price, and the tracking
            // number. A single-item order collapses both onto one row. Read each field from wherever it lives.
            var itemRows = rows.Where(row => table.Text(row, ItemNumber).Length > 0).ToList();
            var summary = rows.FirstOrDefault(row => table.Text(row, ItemNumber).Length == 0);

            if (itemRows.Count == 0)
            {
                skipped += rows.Count; // an order with no item rows cannot be reconciled to inventory
                continue;
            }

            // Money always comes from the summary row when there is one; item rows leave it blank.
            var moneyRow = summary ?? itemRows[0];
            var facts = summary ?? itemRows[0];
            var trackingRow = rows.FirstOrDefault(row => table.Text(row, "Tracking Number").Length > 0) ?? itemRows[0];

            decimal itemsSubtotal = itemRows.Sum(row => table.Money(row, "Sold For") * Math.Max(1, table.Int(row, "Quantity", 1)));
            decimal shipping = table.Money(moneyRow, "Shipping And Handling");
            decimal sellerTax = table.Money(moneyRow, "Seller Collected Tax");
            decimal ebayTax = table.Money(moneyRow, "eBay Collected Tax");

            var lines = itemRows.Select(row => new IngestPlatformOrderLineInput(
                Title: table.TextOrNull(row, "Item Title") ?? "Untitled item",
                UnitPrice: table.Money(row, "Sold For"),
                Quantity: Math.Max(1, table.Int(row, "Quantity", 1)),
                ExternalItemId: table.TextOrNull(row, ItemNumber),
                Sku: table.TextOrNull(row, "Custom Label")))
                .ToList();

            DateTimeOffset orderedAt = table.Date(facts, "Sale Date") ?? DateTimeOffset.UtcNow;

            orders.Add(new IngestPlatformOrderInput(
                ExternalOrderId: orderNumber,
                OrderedAt: orderedAt,
                ItemsSubtotal: itemsSubtotal,
                ShippingCharged: shipping,
                TaxCollected: sellerTax + ebayTax,
                // This report has no fee columns. Reporting 0 is honest; the Sales report supplies the real fees.
                FeeTotal: 0m,
                Lines: lines,
                Platform: "eBay",
                // eBay remits the tax it collected; tax the seller collected is a liability we owe.
                TaxRemittedByPlatform: sellerTax == 0,
                BuyerName: table.TextOrNull(facts, "Buyer Name"),
                BuyerReference: table.TextOrNull(facts, BuyerUsername),
                BuyerEmail: table.TextOrNull(facts, "Buyer Email"),
                BuyerNote: table.TextOrNull(facts, "Buyer Note"),
                ShipToName: table.TextOrNull(facts, "Ship To Name"),
                ShipToPhone: table.TextOrNull(facts, "Ship To Phone"),
                ShipToLine1: table.TextOrNull(facts, "Ship To Address 1"),
                ShipToLine2: table.TextOrNull(facts, "Ship To Address 2"),
                ShipToCity: table.TextOrNull(facts, "Ship To City"),
                ShipToRegion: table.TextOrNull(facts, "Ship To State"),
                ShipToPostalCode: table.TextOrNull(facts, "Ship To Zip"),
                ShipToCountry: table.TextOrNull(facts, "Ship To Country"),
                Carrier: table.TextOrNull(trackingRow, "Shipping Service") ?? table.TextOrNull(facts, "Shipping Service"),
                TrackingNumber: table.TextOrNull(trackingRow, "Tracking Number"),
                PaidAt: table.Date(facts, "Paid On Date"),
                ShippedAt: table.Date(facts, "Shipped On Date"),
                RawJson: JsonSerializer.Serialize(new { orderNumber, rows = rows.Count, multiItem = summary is not null })));
        }

        string? warning = orders.Count == 0
            ? "No order rows were found in the report."
            : skipped > 0 ? $"{skipped} non-order row(s) (trailer or item-less) were skipped." : null;
        return new ParseResult(orders, orders.Count, skipped, warning);
    }
}
