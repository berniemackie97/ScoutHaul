using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StorefrontCommercePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcceptsOffers",
                table: "StoreListings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumOfferAmount",
                table: "StoreListings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumOfferAmount",
                table: "StoreListings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingAmountOverride",
                table: "StoreListings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingPolicy",
                table: "StoreListings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "StoreDefault");

            migrationBuilder.AlterColumn<string>(
                name: "BuyerName",
                table: "StorefrontOffers",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerEmail",
                table: "StorefrontOffers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.Sql("""
                DELETE FROM "CartLines" duplicate
                USING "CartLines" keeper
                WHERE duplicate."OrganizationId" = keeper."OrganizationId"
                  AND duplicate."CartId" = keeper."CartId"
                  AND duplicate."StoreListingId" = keeper."StoreListingId"
                  AND (
                    duplicate."CreatedAt" > keeper."CreatedAt"
                    OR (duplicate."CreatedAt" = keeper."CreatedAt" AND duplicate."Id" > keeper."Id")
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CartLines_OrganizationId_CartId_StoreListingId",
                table: "CartLines",
                columns: new[] { "OrganizationId", "CartId", "StoreListingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CartLines_OrganizationId_CartId_StoreListingId",
                table: "CartLines");

            migrationBuilder.DropColumn(
                name: "AcceptsOffers",
                table: "StoreListings");

            migrationBuilder.DropColumn(
                name: "MaximumOfferAmount",
                table: "StoreListings");

            migrationBuilder.DropColumn(
                name: "MinimumOfferAmount",
                table: "StoreListings");

            migrationBuilder.DropColumn(
                name: "ShippingAmountOverride",
                table: "StoreListings");

            migrationBuilder.DropColumn(
                name: "ShippingPolicy",
                table: "StoreListings");

            migrationBuilder.AlterColumn<string>(
                name: "BuyerName",
                table: "StorefrontOffers",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerEmail",
                table: "StorefrontOffers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);
        }
    }
}
