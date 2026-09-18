using HaulScout.Application.Contracts;
using HaulScout.Application.Imports;
using HaulScout.Domain;

namespace HaulScout.Tests;

/// <summary>
/// Depop's sales export and Poshmark's inventory report, read against their real headers.
///
/// Fixtures are synthetic — the real exports in docs/ carry buyers' names and addresses and are
/// never committed — but every header is copied verbatim from those files, because a parser built
/// against a guessed layout is how an import silently reads the wrong column.
/// </summary>
public sealed class ChannelReportParserTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private const string DepopHeader =
        "\"Date of sale\",\"Time of sale\",\"Date of listing\",\"Bundle\",\"Bundle - amount of items\",\"Buyer\",\"Brand\",\"SKU\",\"Description\",\"Size\",\"Item price\",\"Buyer shipping cost\",\"Total\",\"USPS Cost\",\"Depop fee\",\"Depop Payments fee\",\"Buyer Marketplace Fee\",\"Boosting fee\",\"Payment type\",\"Estimated payout date\",\"Payout arrival date\",\"Category\",\"Name\",\"Address Line 1\",\"Address Line 2\",\"City\",\"State\",\"Post Code\",\"Country\",\"US Sales tax\",\"Refunded to buyer amount\",\"Fees refunded to seller\"";

    private static string DepopRow(
        string date = "07/03/2026",
        string time = "1:46 PM",
        string bundle = "No",
        string bundleItems = "N/A",
        string buyer = "buyer_one",
        string sku = "N/A",
        string description = "Vintage denim jeans size 34",
        string size = "34\"",
        string itemPrice = "$40.00",
        string buyerShipping = "$6.00",
        string total = "$49.20",
        string usps = "$5.10",
        string depopFee = "$4.00",
        string paymentsFee = "$1.60",
        string buyerMarketplaceFee = "$1.75",
        string boosting = "=\"-\"",
        string salesTax = "$3.20",
        string refunded = "$0.00",
        string feesRefunded = "$0.00") =>
        string.Join(",", new[]
        {
            date, time, "07/01/2026", bundle, bundleItems, buyer, "Levi's", sku, description, size, itemPrice, buyerShipping,
            total, usps, depopFee, paymentsFee, buyerMarketplaceFee, boosting, "STRIPE", "07/07/2026", "N/A", "Bottoms",
            "Test Buyer", "1 Test Street", "", "Springfield", "OR", "97477", "US", salesTax, refunded, feesRefunded,
        }.Select(Quote));

    private static string Quote(string cell) => $"\"{cell.Replace("\"", "\"\"")}\"";

    private static string Depop(params string[] rows) => string.Join("\n", [DepopHeader, .. rows]);

    private static IngestPlatformOrderInput Single(DepopSalesReportParser.ParseResult result) => Assert.Single(result.Orders);

    [Fact]
    public void Recognises_a_Depop_sales_export_and_nothing_else()
    {
        Assert.True(DepopSalesReportParser.Matches(Depop(DepopRow())));
        Assert.False(DepopSalesReportParser.Matches(PoshmarkReport(PoshmarkRow())));
        Assert.False(PoshmarkInventoryReportParser.Matches(Depop(DepopRow())));
    }

    [Fact]
    public void Reads_every_figure_the_books_need()
    {
        IngestPlatformOrderInput order = Single(DepopSalesReportParser.Parse(Depop(DepopRow()), Pacific));
        IngestPlatformOrderLineInput line = Assert.Single(order.Lines);

        Assert.Equal("Depop", order.Platform);
        Assert.Equal(40.00m, order.ItemsSubtotal);
        Assert.Equal(6.00m, order.ShippingCharged);
        Assert.Equal(3.20m, order.TaxCollected);
        Assert.True(order.TaxRemittedByPlatform);
        Assert.Equal(5.10m, order.ShippingCost);
        // Depop's own fee and its payments fee; the buyer's marketplace fee is not the seller's cost.
        Assert.Equal(5.60m, order.FeeTotal);
        Assert.Equal(49.20m, line.TotalSales);
        // 40.00 + 6.00 shipping − 5.60 fees − 5.10 label.
        Assert.Equal(35.30m, line.NetSales);
        Assert.Equal("buyer_one", order.BuyerReference);
        Assert.Equal("Springfield", order.ShipToCity);
    }

    [Fact]
    public void Reads_the_sale_time_in_the_business_s_own_zone()
    {
        // 1:46 PM Pacific daylight time is 20:46 UTC.
        IngestPlatformOrderInput order = Single(DepopSalesReportParser.Parse(Depop(DepopRow()), Pacific));

        Assert.Equal(new DateTimeOffset(2026, 7, 3, 20, 46, 0, TimeSpan.Zero), order.OrderedAt);
    }

    [Fact]
    public void Keys_a_sale_the_same_way_on_every_import()
    {
        string csv = Depop(DepopRow());

        Assert.Equal(Single(DepopSalesReportParser.Parse(csv, Pacific)).ExternalOrderId, Single(DepopSalesReportParser.Parse(csv, Pacific)).ExternalOrderId);
    }

    [Fact]
    public void Changing_the_business_time_zone_does_not_re_key_its_sales()
    {
        // Otherwise every Depop sale would come back as a second copy after a settings change.
        string csv = Depop(DepopRow());

        Assert.Equal(Single(DepopSalesReportParser.Parse(csv, Pacific)).ExternalOrderId, Single(DepopSalesReportParser.Parse(csv, Eastern)).ExternalOrderId);
        Assert.Equal(
            Assert.Single(Single(DepopSalesReportParser.Parse(csv, Pacific)).Lines).ExternalItemId,
            Assert.Single(Single(DepopSalesReportParser.Parse(csv, Eastern)).Lines).ExternalItemId);
    }

    [Fact]
    public void Reads_the_spreadsheet_formula_cells_Depop_emits()
    {
        IngestPlatformOrderInput withDash = Single(DepopSalesReportParser.Parse(Depop(DepopRow(boosting: "=\"-\"")), Pacific));
        IngestPlatformOrderInput withAmount = Single(DepopSalesReportParser.Parse(Depop(DepopRow(boosting: "=\"$1.50\"")), Pacific));

        Assert.Equal(5.60m, withDash.FeeTotal);
        Assert.Equal(7.10m, withAmount.FeeTotal);
    }

    [Fact]
    public void Refuses_a_row_with_an_unreadable_amount_and_names_the_column()
    {
        // The old reader turned this into $0.00 without a word.
        DepopSalesReportParser.ParseResult result = DepopSalesReportParser.Parse(Depop(DepopRow(depopFee: "four dollars")), Pacific);

        Assert.Empty(result.Orders);
        DepopSalesReportParser.RowIssue issue = Assert.Single(result.Issues);
        Assert.True(issue.Rejected);
        Assert.Contains("Depop fee", issue.Message);
        Assert.Equal(1, issue.RowNumber);
    }

    [Fact]
    public void Refuses_a_row_with_an_unreadable_date()
    {
        DepopSalesReportParser.ParseResult result = DepopSalesReportParser.Parse(Depop(DepopRow(date: "last Tuesday")), Pacific);

        Assert.Empty(result.Orders);
        Assert.True(Assert.Single(result.Issues).Rejected);
    }

    [Fact]
    public void Keeps_the_good_rows_when_one_is_bad()
    {
        DepopSalesReportParser.ParseResult result = DepopSalesReportParser.Parse(
            Depop(DepopRow(), DepopRow(date: "never", description: "Wool coat")), Pacific);

        Assert.Single(result.Orders);
        Assert.Equal(2, result.RowsRead);
        Assert.Equal(2, Assert.Single(result.Issues).RowNumber);
    }

    [Fact]
    public void Flags_a_bundle_so_the_match_covers_every_item()
    {
        DepopSalesReportParser.ParseResult result = DepopSalesReportParser.Parse(Depop(DepopRow(bundle: "Yes", bundleItems: "3")), Pacific);

        Assert.Single(result.Orders);
        DepopSalesReportParser.RowIssue issue = Assert.Single(result.Issues);
        Assert.False(issue.Rejected);
        Assert.Contains("Bundle of 3 items", issue.Message);
    }

    [Fact]
    public void Says_when_a_sale_was_refunded()
    {
        DepopSalesReportParser.ParseResult result = DepopSalesReportParser.Parse(Depop(DepopRow(refunded: "$51.00", feesRefunded: "$5.60")), Pacific);

        Assert.Single(result.Orders);
        Assert.Contains("$51.00", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Ignores_the_same_sale_listed_twice_in_one_file()
    {
        DepopSalesReportParser.ParseResult result = DepopSalesReportParser.Parse(Depop(DepopRow(), DepopRow()), Pacific);

        Assert.Single(result.Orders);
        Assert.False(Assert.Single(result.Issues).Rejected);
    }

    [Fact]
    public void Reports_the_period_the_file_covers()
    {
        DepopSalesReportParser.ParseResult result = DepopSalesReportParser.Parse(
            Depop(DepopRow(date: "05/02/2026", description: "A"), DepopRow(date: "07/03/2026", description: "B")), Pacific);

        Assert.Equal(new DateTime(2026, 5, 2), result.From!.Value.ToOffset(Pacific.GetUtcOffset(result.From.Value)).Date);
        Assert.Equal(new DateTime(2026, 7, 3), result.To!.Value.ToOffset(Pacific.GetUtcOffset(result.To.Value)).Date);
    }

    [Fact]
    public void Treats_N_A_as_no_SKU()
    {
        Assert.Null(Assert.Single(Single(DepopSalesReportParser.Parse(Depop(DepopRow(sku: "N/A")), Pacific)).Lines).Sku);
        Assert.Equal("SKU-9", Assert.Single(Single(DepopSalesReportParser.Parse(Depop(DepopRow(sku: "SKU-9")), Pacific)).Lines).Sku);
    }

    // ── Poshmark ───────────────────────────────────────────────────────────────

    private const string PoshmarkPreamble =
        "Poshmark Inventory Report for @testcloset\nInventory as of 07/09/2026\nNotes:\n** Only listings that are available are shown on this file.\n** The balance amount on this page should not be used for accounting purposes.\n\"\"";

    private const string PoshmarkHeader =
        "Listing Date,Number of Days Listed,Listing SKU,Item SKU,Listing Title,Department,Category,Subcategory,Brand,Color,Size,NWT,Quantity Available,Likes,Cost Price,Lowest Listed Price,Current Listing Price,Other Info,Seller Shipping Discount";

    private static string PoshmarkRow(
        string date = "06/23/2026",
        string title = "Silk printed blouse",
        string brand = "Chico's",
        string color = "Yellow",
        string size = "2",
        string nwt = "N",
        string quantity = "1",
        string price = "$25.00",
        string itemSku = "") =>
        string.Join(",", new[]
        {
            date, "16", "", itemSku, title, "Women", "Tops", "Blouses", brand, color, size, nwt, quantity, "3", "", "$25.00", price, "", "",
        }.Select(Quote));

    private static string PoshmarkReport(params string[] rows) => string.Join("\n", [PoshmarkPreamble, PoshmarkHeader, .. rows]);

    [Fact]
    public void Reads_available_Poshmark_listings_as_of_the_report_date()
    {
        PoshmarkInventoryReportParser.ParseResult result = PoshmarkInventoryReportParser.Parse(
            PoshmarkReport(PoshmarkRow(), PoshmarkRow(title: "Maxi dress", price: "$100.00", nwt: "Y")), Pacific);

        Assert.Equal(2, result.Listings.Count);
        Assert.All(result.Listings, listing => Assert.Equal(PlatformListingStatus.Active, listing.Status));
        Assert.Equal(100.00m, result.Listings[1].Price);
        Assert.Equal("New with tags", result.Listings[1].Condition);
        Assert.Equal("Women › Tops › Blouses", result.Listings[0].CategoryName);
        // July 9 at midnight Pacific.
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 7, 0, 0, TimeSpan.Zero), result.AsOf);
    }

    [Fact]
    public void Keys_a_Poshmark_listing_the_same_way_on_every_import()
    {
        string csv = PoshmarkReport(PoshmarkRow());

        Assert.Equal(
            Assert.Single(PoshmarkInventoryReportParser.Parse(csv, Pacific).Listings).ExternalListingId,
            Assert.Single(PoshmarkInventoryReportParser.Parse(csv, Pacific).Listings).ExternalListingId);
    }

    [Fact]
    public void Refuses_a_listing_with_no_readable_price()
    {
        PoshmarkInventoryReportParser.ParseResult result = PoshmarkInventoryReportParser.Parse(PoshmarkReport(PoshmarkRow(price: "")), Pacific);

        Assert.Empty(result.Listings);
        Assert.True(Assert.Single(result.Issues).Rejected);
    }

    [Fact]
    public void Says_when_the_report_does_not_state_its_date()
    {
        string withoutDate = string.Join("\n", PoshmarkHeader, PoshmarkRow());

        PoshmarkInventoryReportParser.ParseResult result = PoshmarkInventoryReportParser.Parse(withoutDate, Pacific);

        Assert.Null(result.AsOf);
        Assert.Contains(result.Issues, issue => !issue.Rejected && issue.Message.Contains("date"));
    }

    [Fact]
    public void Tells_apart_two_similar_listings()
    {
        PoshmarkInventoryReportParser.ParseResult result = PoshmarkInventoryReportParser.Parse(
            PoshmarkReport(PoshmarkRow(size: "S"), PoshmarkRow(size: "M")), Pacific);

        Assert.Equal(2, result.Listings.Select(listing => listing.ExternalListingId).Distinct().Count());
    }
}
