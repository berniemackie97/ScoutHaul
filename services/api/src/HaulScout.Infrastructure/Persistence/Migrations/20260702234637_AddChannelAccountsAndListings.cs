using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAccountsAndListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DefaultFeeProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsNativelySynced = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelAccounts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ExternalListingId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ListedPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    ImageHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ListedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SoldAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelListings_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelListings_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelListings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAccounts_OrganizationId_Channel",
                table: "ChannelAccounts",
                columns: new[] { "OrganizationId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAccounts_OrganizationId_DisplayName",
                table: "ChannelAccounts",
                columns: new[] { "OrganizationId", "DisplayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelListings_ChannelAccountId",
                table: "ChannelListings",
                column: "ChannelAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelListings_InventoryItemId",
                table: "ChannelListings",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelListings_OrganizationId_InventoryItemId_ChannelAccou~",
                table: "ChannelListings",
                columns: new[] { "OrganizationId", "InventoryItemId", "ChannelAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelListings_OrganizationId_Status",
                table: "ChannelListings",
                columns: new[] { "OrganizationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelListings");

            migrationBuilder.DropTable(
                name: "ChannelAccounts");
        }
    }
}
