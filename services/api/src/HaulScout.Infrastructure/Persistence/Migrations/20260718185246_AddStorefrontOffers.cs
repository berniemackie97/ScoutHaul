using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorefrontOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    OfferAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ListingPriceSnapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BuyerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    BuyerPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OfferedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResponseNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorefrontOffers_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontOffers_StoreListings_StoreListingId",
                        column: x => x.StoreListingId,
                        principalTable: "StoreListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOffers_InventoryItemId",
                table: "StorefrontOffers",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOffers_OrganizationId_BuyerEmail",
                table: "StorefrontOffers",
                columns: new[] { "OrganizationId", "BuyerEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOffers_OrganizationId_Status",
                table: "StorefrontOffers",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOffers_OrganizationId_StoreListingId_CreatedAt",
                table: "StorefrontOffers",
                columns: new[] { "OrganizationId", "StoreListingId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOffers_StoreListingId",
                table: "StorefrontOffers",
                column: "StoreListingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorefrontOffers");
        }
    }
}
