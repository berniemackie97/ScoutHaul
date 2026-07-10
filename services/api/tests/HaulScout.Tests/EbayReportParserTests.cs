using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Tests;

public class EbayReportParserTests
{
    // A faithful slice of a real eBay Seller Hub sales report: disclaimer preamble, the report date line, the
    // header, then rows — including a quoted title with commas and a $0.00 fee-only artifact row.
    private const string Sample = """
,,,,,,,,,,,,,,
Disclaimers
• This is an analytics report and currently we support downloading top 8000 listings by sold quantity.

"Report for Apr 9, 2026 to Jul 7, 2026"
Listing title,eBay item ID,Store category L1,Store category L2,Quantity sold,Total sales (Includes taxes),Item sales,Taxes and government fees paid by buyer to you,Taxes and government fees paid by buyer to eBay,Shipping and handling paid by buyer to you,Total selling costs,Insertion fees,Optional listing upgrade fees,Final value fees,Promoted Listings - General fees,Promoted Listings - Priority fees,Ads Express fees,Promoted Offsite - Fees,International fees,Other eBay fees,Deposit processing fees,Fee credits,Shipping labels cost (Amount you paid to buy shipping labels on eBay),Net sales (Net of taxes and selling costs),Average Selling price,Quantity sold via promoted listing,Quantity sold via Best Offers,Quantity sold via Seller Initiated Offers,
Primal Groudon-EX 97/98 XY Primal Clash Ultra Rare Full Art Holo Eng 2015,377251318593,-,-,1,$88.12,$77.28,$0.00,$7.15,$3.69,$18.05,$0.00,$0.00,$19.29,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$7.21,$5.97,$62.92,$88.12,0,1,0
The Pokémon Company Charizard EX XY121 XY Promo Holo Eng 2016,377251374220,-,-,1,$0.00,$0.00,$0.00,$0.00,$0.00,$12.34,$0.00,$0.00,$16.90,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$16.50,$11.94,($12.34),$0.00,0,1,0
"Nintendo Mario Kart Wii - Nintendo Wii, Disc w/ Case & Manual, E, NTSC 2008",377272846397,-,-,1,$32.97,$27.00,$0.00,$0.00,$5.97,$10.85,$0.00,$0.00,$4.88,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$5.97,$22.12,$32.97,0,1,0
""";

    private const string AllOrdersSample = """
Order Number,Buyer Username,Buyer Name,Buyer Email,Buyer Note,Ship To Name,Ship To Phone,Ship To Address 1,Ship To Address 2,Ship To City,Ship To State,Ship To Zip,Ship To Country,Sale Date,Paid On Date,Shipped On Date,Shipping Service,Tracking Number,Item Number,Item Title,Custom Label,Quantity,Sold For,Shipping And Handling,Seller Collected Tax,eBay Collected Tax,Total Price
12-34567-89012,lampbuyer,Brass Buyer,buyer@example.com,Please pack well,Brass Buyer,555-1212,1 Main St,Apt 2,Scranton,PA,18503,United States,Jun-30-26,Jun-30-26,Jul-1-26,USPS Ground Advantage,9400111100000000000000,,,,,$125.00,$8.50,$0.00,$10.25,$143.75
12-34567-89012,lampbuyer,Brass Buyer,buyer@example.com,,Brass Buyer,555-1212,1 Main St,Apt 2,Scranton,PA,18503,United States,Jun-30-26,Jun-30-26,Jul-1-26,USPS Ground Advantage,9400111100000000000000,377251318593,Brass Lamp,SKU-LAMP,1,$77.28,$0.00,$0.00,$0.00,$77.28
12-34567-89012,lampbuyer,Brass Buyer,buyer@example.com,,Brass Buyer,555-1212,1 Main St,Apt 2,Scranton,PA,18503,United States,Jun-30-26,Jun-30-26,Jul-1-26,USPS Ground Advantage,9400111100000000000000,377272846397,"Nintendo Mario Kart Wii - Nintendo Wii, Disc w/ Case & Manual, E, NTSC 2008",SKU-WII,1,$47.72,$0.00,$0.00,$0.00,$47.72
""";

    [Fact]
    public void Listing_sales_rows_are_fee_metrics_not_orders()
    {
        EbayReportParser.ParseResult result = EbayReportParser.ParseSalesReport(Sample);

        Assert.Equal(EbayReportParser.EbayReportKind.ListingSales, result.Kind);
        Assert.Empty(result.Orders);
        Assert.Equal(2, result.ListingSales.Count); // 3 data rows, the $0.00 Charizard artifact is skipped
        Assert.Equal(1, result.RowsSkipped);
    }

    [Fact]
    public void Maps_the_exact_fee_stack_and_shipping_label_for_a_row()
    {
        EbayReportParser.EbayListingSalesMetric metric = EbayReportParser.ParseSalesReport(Sample).ListingSales.Single(o => o.ExternalItemId == "377251318593");

        Assert.Equal(77.28m, metric.ItemSales);
        Assert.Equal(3.69m, metric.ShippingCharged);
        Assert.Equal(7.15m, metric.TaxCollected);
        Assert.Equal(12.08m, metric.PlatformFees);       // total selling costs 18.05 − shipping label 5.97
        Assert.Equal(5.97m, metric.SellerShippingCost);  // seller's eBay label cost
        Assert.Equal(62.92m, metric.NetSales);
    }

    [Fact]
    public void Handles_quoted_titles_that_contain_commas()
    {
        EbayReportParser.EbayListingSalesMetric metric = EbayReportParser.ParseSalesReport(Sample).ListingSales.Single(o => o.ExternalItemId == "377272846397");
        Assert.Equal("Nintendo Mario Kart Wii - Nintendo Wii, Disc w/ Case & Manual, E, NTSC 2008", metric.Title);
        Assert.Equal(27.00m, metric.ItemSales);
        Assert.Equal(4.88m, metric.PlatformFees); // 10.85 selling costs − 5.97 label
    }

    [Fact]
    public void All_orders_report_creates_one_real_order_with_multiple_lines()
    {
        EbayReportParser.ParseResult result = EbayReportParser.ParseSalesReport(AllOrdersSample);

        Assert.Equal(EbayReportParser.EbayReportKind.AllOrders, result.Kind);
        IngestPlatformOrderInput order = Assert.Single(result.Orders);
        Assert.Equal("12-34567-89012", order.ExternalOrderId);
        Assert.Equal("lampbuyer", order.BuyerReference);
        Assert.Equal("buyer@example.com", order.BuyerEmail);
        Assert.Equal("9400111100000000000000", order.TrackingNumber);
        Assert.Equal("USPS Ground Advantage", order.Carrier);
        Assert.Equal(2026, order.OrderedAt.Year);
        Assert.Equal(6, order.OrderedAt.Month);
        Assert.Equal(30, order.OrderedAt.Day);
        Assert.Equal(125.00m, order.ItemsSubtotal);
        Assert.Equal(8.50m, order.ShippingCharged);
        Assert.Equal(10.25m, order.TaxCollected);
        Assert.True(order.TaxRemittedByPlatform);
        Assert.Equal(2, order.Lines.Count);
        Assert.Contains(order.Lines, line => line.ExternalItemId == "377251318593" && line.Title == "Brass Lamp");
        Assert.Contains(order.Lines, line => line.ExternalItemId == "377272846397" && line.Title.Contains("Disc w/ Case"));
    }

    [Fact]
    public void Rejects_a_file_that_is_not_an_ebay_report()
    {
        EbayReportParser.ParseResult result = EbayReportParser.ParseSalesReport("name,amount\nfoo,10");
        Assert.Empty(result.Orders);
        Assert.NotNull(result.Warning);
    }
}
