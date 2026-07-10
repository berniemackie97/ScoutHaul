using HaulScout.Application.Imports;
using HaulScout.Domain;

namespace HaulScout.Tests;

/// <summary>Pinned against the real eBay Active Listings header (30 columns). Row data is synthetic.</summary>
public class EbayActiveListingsParserTests
{
    private const string Header =
        "Item number,Title,Variation details,Custom label (SKU),Available quantity,Format,Currency,Start price,Auction Buy It Now price,Reserve price,Current price,Sold quantity,Watchers,Bids,Start date,End date,eBay category 1 name,eBay category 1 number,eBay category 2 name,eBay category 2 number,Condition,CD:Professional Grader - (ID: 27501),CD:Grade - (ID: 27502),CDA:Certification Number - (ID: 27503),CD:Card Condition - (ID: 40001),eBay Product ID(ePID),Listing site,P:UPC,P:EAN,P:ISBN";

    private static string Row(
        string item, string title, string available, string currentPrice,
        string sku = "", string qtySold = "0", string format = "FixedPrice",
        string startPrice = "", string category = "Trading Card Singles", string condition = "Ungraded")
    {
        var cells = new string[30];
        Array.Fill(cells, "");
        cells[0] = item;
        cells[1] = title;
        cells[3] = sku;
        cells[4] = available;
        cells[5] = format;
        cells[6] = "USD";
        cells[7] = startPrice;
        cells[10] = currentPrice;
        cells[11] = qtySold;
        cells[14] = "Jun-01-26 10:00:00";
        cells[16] = category;
        cells[20] = condition;
        return string.Join(",", cells.Select(c => $"\"{c}\""));
    }

    private static string Report(params string[] rows) => string.Join("\n", new[] { Header }.Concat(rows));

    [Fact]
    public void Detects_the_report_by_header_signature()
    {
        Assert.True(EbayActiveListingsParser.Matches(Report(Row("377", "Card", "1", "$9.99"))));
        Assert.False(EbayActiveListingsParser.Matches("name,price\nfoo,1"));
    }

    [Fact]
    public void Maps_the_listing_fields()
    {
        var result = EbayActiveListingsParser.Parse(Report(
            Row("377251497198", "Houndoom EX 153/162 Full Art", "1", "$29.50", sku: "HND-153", qtySold: "0", category: "CCG Individual Cards", condition: "Ungraded")));

        var listing = Assert.Single(result.Listings);
        Assert.Equal("377251497198", listing.ExternalListingId);
        Assert.Equal("Houndoom EX 153/162 Full Art", listing.Title);
        Assert.Equal(29.50m, listing.Price);
        Assert.Equal(PlatformListingStatus.Active, listing.Status);
        Assert.Equal(1, listing.Quantity);
        Assert.Equal("HND-153", listing.Sku);
        Assert.Equal("CCG Individual Cards", listing.CategoryName);
        Assert.Equal("Ungraded", listing.Condition);
        Assert.Equal("eBay", listing.Platform);
        Assert.Equal("https://www.ebay.com/itm/377251497198", listing.Url);
        Assert.NotNull(listing.ListedAt);
    }

    [Fact]
    public void Falls_back_to_start_price_when_current_price_is_blank()
    {
        var listing = Assert.Single(EbayActiveListingsParser.Parse(Report(
            Row("1", "Auction card", "1", currentPrice: "", startPrice: "$4.25"))).Listings);
        Assert.Equal(4.25m, listing.Price);
    }

    [Fact]
    public void Rows_without_an_item_or_title_are_skipped()
    {
        var result = EbayActiveListingsParser.Parse(Report(
            Row("", "No item", "1", "$5.00"),
            Row("377", "Good", "1", "$5.00")));
        Assert.Single(result.Listings);
        Assert.Equal(1, result.RowsSkipped);
    }

    [Fact]
    public void Parses_a_real_export_when_one_is_present()
    {
        string? path = Directory.EnumerateFiles(FindRepoRoot(), "eBay-all-active-listings-report-*.csv", SearchOption.AllDirectories).FirstOrDefault();
        if (path is null) return; // CI: gitignored PII file, nothing to check

        var result = EbayActiveListingsParser.Parse(File.ReadAllText(path));
        Assert.NotEmpty(result.Listings);
        Assert.All(result.Listings, listing =>
        {
            Assert.False(string.IsNullOrWhiteSpace(listing.ExternalListingId));
            Assert.False(string.IsNullOrWhiteSpace(listing.Title));
            Assert.Equal(PlatformListingStatus.Active, listing.Status);
        });
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
