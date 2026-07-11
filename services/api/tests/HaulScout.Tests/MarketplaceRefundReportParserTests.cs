using HaulScout.Application.Imports;

namespace HaulScout.Tests;

public class MarketplaceRefundReportParserTests
{
    private const string EbayListingSales = """
"Report for Jan 1, 2026 to Jul 10, 2026"
Listing title,eBay item ID,Quantity sold,Total sales (Includes taxes),Item sales,Taxes and government fees paid by buyer to you,Taxes and government fees paid by buyer to eBay,Shipping and handling paid by buyer to you,Total selling costs,Insertion fees,Optional listing upgrade fees,Final value fees,Promoted Listings - General fees,Promoted Listings - Priority fees,Ads Express fees,Promoted Offsite - Fees,International fees,Other eBay fees,Deposit processing fees,Fee credits,Shipping labels cost (Amount you paid to buy shipping labels on eBay),Net sales (Net of taxes and selling costs),Average Selling price,Quantity sold via promoted listing,Quantity sold via Best Offers,Quantity sold via Seller Initiated Offers,
Good Sale,377251318593,1,$88.12,$77.28,$0.00,$7.15,$3.69,$18.05,$0.00,$0.00,$19.29,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$7.21,$5.97,$62.92,$88.12,0,1,0
Refunded Card,377251374220,1,$0.00,$0.00,$0.00,$0.00,$0.00,$12.34,$0.00,$0.00,$16.90,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$0.00,$16.50,$11.94,($12.34),$0.00,0,1,0
""";

    private const string DepopSales = """"
"Date of sale","Time of sale","Buyer","Brand","SKU","Description","Size","Item price","Buyer shipping cost","Total","US Sales tax","Refunded to buyer amount","Fees refunded to seller"
"07/03/2026","1:46 PM","strickss","Vans","N/A","Vans Shorts Mens 34 Blue Chino Denim Casual Skate 5 pockets
#skater #summer","34""","$5.00","$0.00","$5.42","$0.42","$6.37","$2.00"
"""";

    [Fact]
    public void Ebay_listing_sales_extracts_only_refund_fee_credit_rows()
    {
        MarketplaceRefundReportParser.ParseResult result = MarketplaceRefundReportParser.Parse("eBay", EbayListingSales);

        Assert.Equal(MarketplaceRefundReportParser.MarketplaceRefundReportKind.EbayListingSales, result.Kind);
        MarketplaceRefundReportParser.MarketplaceRefundEvent item = Assert.Single(result.Events);
        Assert.Equal("377251374220", item.ExternalItemId);
        Assert.Equal(16.50m, item.FeeCredit);
        Assert.True(item.FullRefundInferred);
        Assert.Equal(1, result.RowsSkipped);
    }

    [Fact]
    public void Depop_sales_extracts_refund_amount_and_fee_credit()
    {
        MarketplaceRefundReportParser.ParseResult result = MarketplaceRefundReportParser.Parse("Depop", DepopSales);

        Assert.Equal(MarketplaceRefundReportParser.MarketplaceRefundReportKind.DepopSales, result.Kind);
        MarketplaceRefundReportParser.MarketplaceRefundEvent item = Assert.Single(result.Events);
        Assert.Equal("Depop", item.Platform);
        Assert.Null(item.Sku);
        Assert.Contains("Vans Shorts", item.Title);
        Assert.Equal(5.00m, item.ItemPrice);
        Assert.Equal(6.37m, item.BuyerRefundTotal);
        Assert.Equal(2.00m, item.FeeCredit);
        Assert.Equal(2026, item.SoldAt!.Value.Year);
    }
}
