using HaulScout.Application.Imports;

namespace HaulScout.Tests;

/// <summary>
/// Pinned against the REAL eBay All Orders header (82 columns, verbatim). Buyer rows are synthetic — the header is
/// what drifts and what we must detect; buyer data is PII and never enters the repo.
/// </summary>
public class EbayOrdersReportParserTests
{
    // The leading comma row is eBay's own preamble — the header is not the first line.
    private const string Header =
        "\"Sales Record Number\",\"Order Number\",\"Buyer Username\",\"Buyer Name\",\"Buyer Email\",\"Buyer Note\",Buyer Address 1,Buyer Address 2,Buyer City,\"Buyer State\",\"Buyer Zip\",\"Buyer Country\",\"Buyer Tax Identifier Name\",\"Buyer Tax Identifier Value\",Ship To Name,\"Ship To Phone\",Ship To Address 1,Ship To Address 2,Ship To City,\"Ship To State\",\"Ship To Zip\",\"Ship To Country\",\"Item Number\",\"Item Title\",Custom Label,\"Sold Via Promoted Listings\",\"Quantity\",\"Sold For\",\"Shipping And Handling\",Item Location,\"Item Zip Code\",\"Item Country\",\"eBay Collect And Remit Tax Rate\",\"eBay Collect And Remit Tax Type\",\"eBay Reference Name\",eBay Reference Value,Tax Status,\"Seller Collected Tax\",\"eBay Collected Tax\",\"Electronic Waste Recycling Fee\",\"Mattress Recycling Fee\",\"Battery Recycling Fee\",\"White Goods Disposal Tax\",\"Tire Recycling Fee\",\"Additional Fee\",\"Lumber Fee\",\"Prepaid Wireless Fee\",\"Road Improvement And Food Delivery Fee\",\"eBay Collected Charges\",\"Total Price\",\"eBay Collected Tax and Fees Included in Total\",\"Payment Method\",\"Sale Date\",\"Paid On Date\",\"Ship By Date\",\"Minimum Estimated Delivery Date\",\"Maximum Estimated Delivery Date\",\"Shipped On Date\",\"Feedback Left\",\"Feedback Received\",\"My Item Note\",\"PayPal Transaction ID\",\"Shipping Service\",\"Tracking Number\",\"Transaction ID\",Variation Details,\"Global Shipping Program\",\"Global Shipping Reference ID\",\"Click And Collect\",\"Click And Collect Reference Number\",\"eBay Plus\",\"Authenticity Verification Program\",\"Authenticity Verification Status\",\"Authenticity Verification Outcome Reason\",\"PSA Vault Program\",\"Vault Fulfillment Type\",\"eBay Fulfillment Program\",Tax City,\"Tax State\",\"Tax Zip\",\"Tax Country\",\"eBay International Shipping\"";

    /// <summary>Builds one row with only the fields the parser reads; the rest stay blank, as eBay leaves them.</summary>
    private static string Row(
        string orderNumber, string itemNumber, string title, string soldFor, string shipping,
        string buyer = "buyer_one", string ebayTax = "$0.00", string sellerTax = "$0.00",
        string tracking = "", string service = "", string qty = "1")
    {
        var cells = new string[82];
        Array.Fill(cells, "");
        cells[1] = orderNumber;
        cells[2] = buyer;
        cells[3] = "Test Buyer";
        cells[4] = "buyer@example.com";
        cells[14] = "Test Buyer";      // Ship To Name
        cells[16] = "1 Main St";       // Ship To Address 1
        cells[18] = "Springfield";     // Ship To City
        cells[19] = "IL";              // Ship To State
        cells[20] = "62704";           // Ship To Zip
        cells[21] = "US";              // Ship To Country
        cells[22] = itemNumber;
        cells[23] = title;
        cells[26] = qty;
        cells[27] = soldFor;
        cells[28] = shipping;
        cells[37] = sellerTax;
        cells[38] = ebayTax;
        cells[52] = "Jul-05-26 10:00:00";  // Sale Date
        cells[53] = "Jul-05-26 10:05:00";  // Paid On Date
        cells[57] = "Jul-06-26 09:00:00";  // Shipped On Date
        cells[62] = service;
        cells[63] = tracking;
        return string.Join(",", cells.Select(c => $"\"{c}\""));
    }

    /// <summary>The header row of a multi-item order: buyer + money, no item number, no tracking.</summary>
    private static string SummaryRow(string orderNumber, string qty, string soldFor, string shipping, string ebayTax = "$0.00", string sellerTax = "$0.00")
    {
        var cells = new string[82];
        Array.Fill(cells, "");
        cells[1] = orderNumber;
        cells[2] = "buyer_one";
        cells[3] = "Test Buyer";
        cells[4] = "buyer@example.com";
        cells[14] = "Test Buyer";
        cells[16] = "1 Main St";
        cells[18] = "Springfield";
        cells[19] = "IL";
        cells[20] = "62704";
        cells[21] = "US";
        cells[26] = qty;
        cells[27] = soldFor;
        cells[28] = shipping;
        cells[37] = sellerTax;
        cells[38] = ebayTax;
        cells[52] = "Jul-05-26 10:00:00";
        return string.Join(",", cells.Select(c => $"\"{c}\""));
    }

    /// <summary>eBay's file trailer: only a record count, which lands in the Order Number column.</summary>
    private static string TrailerRow()
    {
        var cells = new string[82];
        Array.Fill(cells, "");
        cells[0] = "36";
        cells[1] = "record(s) downloaded";
        return string.Join(",", cells.Select(c => $"\"{c}\""));
    }

    private static string Report(params string[] rows) =>
        string.Join("\n", new[] { new string(',', 40), Header }.Concat(rows));

    [Fact]
    public void Detects_the_report_by_header_signature()
    {
        Assert.True(EbayOrdersReportParser.Matches(Report(Row("02-1", "377300562852", "Lamp", "$5.00", "$6.72"))));
        Assert.False(EbayOrdersReportParser.Matches("name,amount\nfoo,10"));
    }

    [Fact]
    public void Uses_the_real_order_number_not_the_item_number()
    {
        // This is the exact bug that produced orphaned, buyer-less orders named after an eBay item id.
        var result = EbayOrdersReportParser.Parse(Report(
            Row("02-14887-25846", "377300562852", "Brass Lamp", "$5.00", "$6.72",
                buyer: "rugmossburrsmith", tracking: "9400108106245346490169", service: "USPS First Class")));

        var order = Assert.Single(result.Orders);
        Assert.Equal("02-14887-25846", order.ExternalOrderId);          // the order, not the item
        Assert.Equal("rugmossburrsmith", order.BuyerReference);
        Assert.Equal("buyer@example.com", order.BuyerEmail);
        Assert.Equal("9400108106245346490169", order.TrackingNumber);
        Assert.Equal("Springfield", order.ShipToCity);
        Assert.Equal(5.00m, order.ItemsSubtotal);
        Assert.Equal(6.72m, order.ShippingCharged);
        Assert.Equal("377300562852", Assert.Single(order.Lines).ExternalItemId);
    }

    /// <summary>
    /// A multi-item eBay order is header/detail: a SUMMARY row (no item number) holds buyer, shipping and tax; the
    /// ITEM rows hold the items and the tracking number, and leave shipping/tax blank. Reading money from the item
    /// rows — the obvious-looking implementation — imports every multi-item order with $0 shipping and $0 tax.
    /// </summary>
    [Fact]
    public void A_multi_item_order_reads_money_from_the_summary_row_and_tracking_from_the_item_rows()
    {
        var result = EbayOrdersReportParser.Parse(Report(
            SummaryRow("11-14775-12021", qty: "3", soldFor: "$29.50", shipping: "$6.36", ebayTax: "$1.77"),
            Row("11-14775-12021", "377251497198", "Card A", "$20.00", "", tracking: "9400108106245346", service: "USPS Ground Advantage"),
            Row("11-14775-12021", "377252251780", "Card B", "$6.00", "", tracking: "9400108106245346"),
            Row("11-14775-12021", "377252265234", "Card C", "$3.50", "", tracking: "9400108106245346")));

        var order = Assert.Single(result.Orders);
        Assert.Equal(3, order.Lines.Count);                 // one order, three lines — not three fake orders
        Assert.Equal(29.50m, order.ItemsSubtotal);          // summed from the item rows
        Assert.Equal(6.36m, order.ShippingCharged);         // ONLY on the summary row
        Assert.Equal(1.77m, order.TaxCollected);            // ONLY on the summary row
        Assert.Equal("9400108106245346", order.TrackingNumber); // ONLY on the item rows
        Assert.Equal("USPS Ground Advantage", order.Carrier);
    }

    [Fact]
    public void A_single_item_order_carries_everything_on_one_row()
    {
        var result = EbayOrdersReportParser.Parse(Report(
            Row("02-14887-25846", "377300562852", "Brass Lamp", "$5.00", "$6.72", tracking: "94001081062453")));

        var order = Assert.Single(result.Orders);
        Assert.Equal(5.00m, order.ItemsSubtotal);
        Assert.Equal(6.72m, order.ShippingCharged);
        Assert.Equal("94001081062453", order.TrackingNumber);
    }

    [Fact]
    public void The_trailer_row_is_not_an_order()
    {
        // eBay ends the file with "36 record(s) downloaded", which lands in the Order Number column.
        var result = EbayOrdersReportParser.Parse(Report(
            Row("02-1", "377", "Lamp", "$5.00", "$6.72"),
            TrailerRow()));

        Assert.Single(result.Orders);
        Assert.Equal(1, result.RowsSkipped);
    }

    [Fact]
    public void It_never_invents_fees_because_this_report_has_none()
    {
        var result = EbayOrdersReportParser.Parse(Report(Row("02-1", "377", "Lamp", "$5.00", "$6.72")));
        Assert.Equal(0m, Assert.Single(result.Orders).FeeTotal);
    }

    [Fact]
    public void Tax_remittance_follows_who_collected_it()
    {
        var remitted = EbayOrdersReportParser.Parse(Report(Row("A", "1", "X", "$10.00", "$0.00", ebayTax: "$1.50")));
        Assert.True(Assert.Single(remitted.Orders).TaxRemittedByPlatform);
        Assert.Equal(1.50m, Assert.Single(remitted.Orders).TaxCollected);

        var sellerOwes = EbayOrdersReportParser.Parse(Report(Row("B", "1", "X", "$10.00", "$0.00", sellerTax: "$2.00")));
        Assert.False(Assert.Single(sellerOwes.Orders).TaxRemittedByPlatform);
    }

    [Fact]
    public void Rows_without_an_order_or_item_number_are_skipped_not_guessed()
    {
        var result = EbayOrdersReportParser.Parse(Report(
            Row("", "377", "Orphan", "$5.00", "$0.00"),
            Row("02-1", "378", "Good", "$5.00", "$0.00")));

        Assert.Single(result.Orders);
        Assert.Equal(1, result.RowsSkipped);
        Assert.NotNull(result.Warning);
    }

    /// <summary>
    /// Smoke test against a real, un-redacted export when one is present on the developer's machine. The file is
    /// gitignored (buyer PII), so this is a no-op in CI — but locally it proves the parser against the genuine
    /// article rather than only against a sample we wrote ourselves.
    /// </summary>
    [Fact]
    public void Parses_a_real_export_when_one_is_present()
    {
        string? path = Directory.EnumerateFiles(FindRepoRoot(), "ebay-all-orders-report-*.csv", SearchOption.AllDirectories).FirstOrDefault();
        if (path is null) return; // CI: nothing to check

        var result = EbayOrdersReportParser.Parse(File.ReadAllText(path));
        Assert.NotEmpty(result.Orders);
        Assert.All(result.Orders, order =>
        {
            Assert.False(string.IsNullOrWhiteSpace(order.ExternalOrderId));
            Assert.NotEmpty(order.Lines);
            Assert.Equal(0m, order.FeeTotal); // this report never carries fees
        });
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
