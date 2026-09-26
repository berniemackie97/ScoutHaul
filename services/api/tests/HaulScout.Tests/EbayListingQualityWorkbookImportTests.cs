using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Imports;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

using HaulScout.Application.Channels;

using HaulScout.Infrastructure.Channels;

namespace HaulScout.Tests;

public sealed class EbayListingQualityWorkbookImportTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    [Fact]
    public void Parser_extracts_listing_id_from_title_hyperlink_and_quality_metrics()
    {
        byte[] workbook = BuildListingQualityWorkbook();

        EbayListingQualityWorkbookParser.ParseResult result = EbayListingQualityWorkbookParser.Parse(workbook, DateTimeOffset.UtcNow);

        EbayListingQualityWorkbookParser.ListingQualityRow row = Assert.Single(result.Rows);
        Assert.Equal("377285556552", row.ExternalListingId);
        Assert.Equal("Chicos Silk Blend Watercolor Floral Open Front Cardigan Jacket Topper Boho W1", row.Title);
        Assert.Equal("Tops", row.CategoryName);
        Assert.Equal("Used", row.ListingCondition);
        Assert.Equal("$1 - $19", row.PriceRange);
        Assert.Equal(TimeSpan.Zero, row.ReportGeneratedAt.Offset);
        Assert.Equal(93m, row.DailyImpressions);
        Assert.Equal(0.012143857m, row.ClickThroughRate);
        Assert.Null(row.SalesConversionRate);
        Assert.Equal(0.02m, row.PromotedListingsAdRate);
        Assert.Equal(6, row.RecommendedItemSpecificsFilled);
        Assert.Equal(4, row.PhotoCount);
        Assert.True(row.FreeShipping);
        Assert.True(row.PromotedListings);
    }

    [Fact]
    public async Task Import_listing_quality_workbook_upserts_snapshot_and_links_existing_platform_listing()
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(nameof(Import_listing_quality_workbook_upserts_snapshot_and_links_existing_platform_listing))
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        await using HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.PlatformListings.Add(new PlatformListing
        {
            OrganizationId = org,
            Platform = "eBay",
            ExternalListingId = "377285556552",
            Title = "Chicos Silk Blend Watercolor Floral Open Front Cardigan Jacket Topper Boho W1",
            Price = 20m,
        });
        await db.SaveChangesAsync();

        PlatformListingsController controller = new(db, new StubCurrentUser(org, user), new PlatformListingImportService(db, new StubCurrentUser(org, user)), new SoldListingService(new EfSoldListingStore(db), new StubCurrentUser(org, user), TimeProvider.System));
        string content = Convert.ToBase64String(BuildListingQualityWorkbook());

        ActionResult<ImportPlatformListingQualityResultDto> first = await controller.ImportEbayListingQualityWorkbook(
            new ImportPlatformListingQualityWorkbookRequest("eBay", "quality.xlsx", content),
            CancellationToken.None);
        ActionResult<ImportPlatformListingQualityResultDto> second = await controller.ImportEbayListingQualityWorkbook(
            new ImportPlatformListingQualityWorkbookRequest("eBay", "quality.xlsx", content),
            CancellationToken.None);

        Assert.Equal(1, first.Value!.Imported);
        Assert.Equal(1, first.Value.MatchedListings);
        Assert.Equal(0, first.Value.UnmatchedListings);
        Assert.Equal(0, second.Value!.Imported);
        Assert.Equal(1, second.Value.Updated);
        PlatformListingQualitySnapshot snapshot = await db.PlatformListingQualitySnapshots.SingleAsync();
        PlatformListing listing = await db.PlatformListings.SingleAsync();
        Assert.Equal(listing.Id, snapshot.PlatformListingId);
        Assert.Equal(93m, snapshot.DailyImpressions);
    }

    private static byte[] BuildListingQualityWorkbook()
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            AddEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            AddEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Tops" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """);
            AddEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            AddEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://www.ebay.com/itm/377285556552" TargetMode="External"/>
                </Relationships>
                """);

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XDocument sheet = new(
                new XElement(ns + "worksheet",
                    new XAttribute(XNamespace.Xmlns + "r", rel),
                    new XElement(ns + "sheetData",
                        Row(ns, 3,
                            Text(ns, "B3", "Category: Tops, Listings condition: Used, Price range: $1 - $19"),
                            Text(ns, "E3", "Report generated: US - 07/14/2026 07-30 PST")),
                        Row(ns, 44,
                            Text(ns, "B44", "Item title"),
                            Text(ns, "E44", "Action"),
                            Text(ns, "F44", "Daily impressions per listing"),
                            Text(ns, "G44", "Click-through rate"),
                            Text(ns, "H44", "Sales conversion rate"),
                            Text(ns, "I44", "Promoted listings ad rate"),
                            Text(ns, "J44", "Recommended item specifics filled"),
                            Text(ns, "K44", "Recommended item specifics to add"),
                            Text(ns, "L44", "Brand"),
                            Text(ns, "M44", "MPN"),
                            Text(ns, "N44", "UPC"),
                            Text(ns, "O44", "Number of photos"),
                            Text(ns, "P44", "Number of keywords in title"),
                            Text(ns, "Q44", "Free shipping"),
                            Text(ns, "R44", "Estimated handling time"),
                            Text(ns, "S44", "Accepts returns"),
                            Text(ns, "T44", "Seller paid returns"),
                            Text(ns, "U44", "Promoted listings"),
                            Text(ns, "V44", "Best Offer")),
                        Row(ns, 45,
                            Text(ns, "B45", "Chicos Silk Blend Watercolor Floral Open Front Cardigan Jacket Topper Boho W1"),
                            Text(ns, "E45", "Edit"),
                            Number(ns, "F45", "93"),
                            Number(ns, "G45", "0.012143857"),
                            Text(ns, "H45", "No sales"),
                            Number(ns, "I45", "0.02"),
                            Number(ns, "J45", "6"),
                            Text(ns, "K45", "Color"),
                            Text(ns, "L45", "Chico's"),
                            Text(ns, "M45", "Not provided"),
                            Text(ns, "N45", "123456789012"),
                            Number(ns, "O45", "4"),
                            Number(ns, "P45", "12"),
                            Text(ns, "Q45", "✔"),
                            Number(ns, "R45", "1"),
                            Text(ns, "S45", "✔"),
                            Text(ns, "T45", "Not enabled"),
                            Text(ns, "U45", "✔"),
                            Text(ns, "V45", "✔"))),
                    new XElement(ns + "hyperlinks",
                        new XElement(ns + "hyperlink",
                            new XAttribute("ref", "B45"),
                            new XAttribute(rel + "id", "rId1")))));
            AddEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString(SaveOptions.DisableFormatting));
        }
        return stream.ToArray();
    }

    private static XElement Row(XNamespace ns, int index, params XElement[] cells) =>
        new(ns + "row", new XAttribute("r", index.ToString()), cells);

    private static XElement Text(XNamespace ns, string reference, string value) =>
        new(ns + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(ns + "is", new XElement(ns + "t", value)));

    private static XElement Number(XNamespace ns, string reference, string value) =>
        new(ns + "c", new XAttribute("r", reference), new XElement(ns + "v", value));

    private static void AddEntry(ZipArchive archive, string name, string contents)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }
}
