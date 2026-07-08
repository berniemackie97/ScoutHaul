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

    [Fact]
    public void Parses_rows_skips_the_preamble_and_the_zero_value_artifact()
    {
        var result = EbayReportParser.ParseSalesReport(Sample);

        Assert.Equal(2, result.Orders.Count); // 3 data rows, the $0.00 Charizard row is skipped
        Assert.Equal(1, result.RowsSkipped);
        Assert.All(result.Orders, order => Assert.Equal("eBay", order.Platform));
    }

    [Fact]
    public void Maps_the_exact_fee_stack_and_shipping_label_for_a_row()
    {
        var order = EbayReportParser.ParseSalesReport(Sample).Orders.Single(o => o.ExternalOrderId == "377251318593");

        Assert.Equal(77.28m, order.ItemsSubtotal);
        Assert.Equal(3.69m, order.ShippingCharged);
        Assert.Equal(7.15m, order.TaxCollected);         // buyer→eBay tax (remitted)
        Assert.True(order.TaxRemittedByPlatform);         // nothing was collected "to you"
        Assert.Equal(12.08m, order.FeeTotal);            // total selling costs 18.05 − shipping label 5.97
        Assert.Equal(5.97m, order.ShippingCost);         // seller's eBay label cost
        var line = Assert.Single(order.Lines);
        Assert.Equal("377251318593", line.ExternalItemId);
        Assert.Equal(77.28m, line.UnitPrice);
    }

    [Fact]
    public void Handles_quoted_titles_that_contain_commas()
    {
        var order = EbayReportParser.ParseSalesReport(Sample).Orders.Single(o => o.ExternalOrderId == "377272846397");
        Assert.Equal("Nintendo Mario Kart Wii - Nintendo Wii, Disc w/ Case & Manual, E, NTSC 2008", order.Lines.Single().Title);
        Assert.Equal(27.00m, order.ItemsSubtotal);
        Assert.Equal(4.88m, order.FeeTotal); // 10.85 selling costs − 5.97 label
    }

    [Fact]
    public void Dates_rows_to_the_reports_end_date()
    {
        var order = EbayReportParser.ParseSalesReport(Sample).Orders.First();
        Assert.Equal(2026, order.OrderedAt.Year);
        Assert.Equal(7, order.OrderedAt.Month);
        Assert.Equal(7, order.OrderedAt.Day);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_an_ebay_report()
    {
        var result = EbayReportParser.ParseSalesReport("name,amount\nfoo,10");
        Assert.Empty(result.Orders);
        Assert.NotNull(result.Warning);
    }
}
