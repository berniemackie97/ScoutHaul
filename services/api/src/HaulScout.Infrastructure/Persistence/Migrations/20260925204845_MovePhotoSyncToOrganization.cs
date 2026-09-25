using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovePhotoSyncToOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MarketplacePhotoSyncEnabled",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // An organization that had photo sync on for any account keeps it on: the switch moved, the answer did not.
            migrationBuilder.Sql(
                """
                UPDATE "Organizations" SET "MarketplacePhotoSyncEnabled" = true
                WHERE EXISTS (
                    SELECT 1 FROM "ChannelAccounts"
                    WHERE "ChannelAccounts"."OrganizationId" = "Organizations"."Id"
                      AND "ChannelAccounts"."PhotoSyncEnabled");
                """);

            migrationBuilder.DropColumn(
                name: "PhotoSyncEnabled",
                table: "ChannelAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarketplacePhotoSyncEnabled",
                table: "Organizations");

            migrationBuilder.AddColumn<bool>(
                name: "PhotoSyncEnabled",
                table: "ChannelAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
