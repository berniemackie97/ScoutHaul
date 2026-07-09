using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ExternalListingId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    QuantitySold = table.Column<int>(type: "integer", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Format = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Sku = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Condition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PrimaryImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ListedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReconcileStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ReconciledInventoryItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReconciledChannelListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ScrapedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformListings_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlatformListings_InventoryItems_ReconciledInventoryItemId",
                        column: x => x.ReconciledInventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlatformListings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformListings_ChannelAccountId",
                table: "PlatformListings",
                column: "ChannelAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformListings_OrganizationId_Platform_ExternalListingId",
                table: "PlatformListings",
                columns: new[] { "OrganizationId", "Platform", "ExternalListingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformListings_OrganizationId_ReconcileStatus",
                table: "PlatformListings",
                columns: new[] { "OrganizationId", "ReconcileStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformListings_ReconciledInventoryItemId",
                table: "PlatformListings",
                column: "ReconciledInventoryItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformListings");
        }
    }
}
