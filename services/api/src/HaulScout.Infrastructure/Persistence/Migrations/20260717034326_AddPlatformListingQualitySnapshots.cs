using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformListingQualitySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformListingQualitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ExternalListingId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ListingCondition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PriceRange = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ReportGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DailyImpressions = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ClickThroughRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    SalesConversionRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    PromotedListingsAdRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    RecommendedItemSpecificsFilled = table.Column<int>(type: "integer", nullable: true),
                    RecommendedItemSpecificsToAdd = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Brand = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Mpn = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Upc = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PhotoCount = table.Column<int>(type: "integer", nullable: true),
                    TitleKeywordCount = table.Column<int>(type: "integer", nullable: true),
                    FreeShipping = table.Column<bool>(type: "boolean", nullable: true),
                    EstimatedHandlingDays = table.Column<int>(type: "integer", nullable: true),
                    AcceptsReturns = table.Column<bool>(type: "boolean", nullable: true),
                    SellerPaidReturns = table.Column<bool>(type: "boolean", nullable: true),
                    PromotedListings = table.Column<bool>(type: "boolean", nullable: true),
                    BestOffer = table.Column<bool>(type: "boolean", nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformListingQualitySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformListingQualitySnapshots_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlatformListingQualitySnapshots_PlatformListings_PlatformLi~",
                        column: x => x.PlatformListingId,
                        principalTable: "PlatformListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformListingQualitySnapshots_OrganizationId_Platform_Ext~",
                table: "PlatformListingQualitySnapshots",
                columns: new[] { "OrganizationId", "Platform", "ExternalListingId", "ReportGeneratedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformListingQualitySnapshots_OrganizationId_PlatformList~",
                table: "PlatformListingQualitySnapshots",
                columns: new[] { "OrganizationId", "PlatformListingId", "ReportGeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformListingQualitySnapshots_PlatformListingId",
                table: "PlatformListingQualitySnapshots",
                column: "PlatformListingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformListingQualitySnapshots");
        }
    }
}
