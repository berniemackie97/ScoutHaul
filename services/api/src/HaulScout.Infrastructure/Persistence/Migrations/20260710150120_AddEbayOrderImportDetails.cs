using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEbayOrderImportDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerEmail",
                table: "PlatformOrders",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerNote",
                table: "PlatformOrders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "PlatformOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                table: "PlatformOrders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountry",
                table: "PlatformOrders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToLine1",
                table: "PlatformOrders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToLine2",
                table: "PlatformOrders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToName",
                table: "PlatformOrders",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToPhone",
                table: "PlatformOrders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToPostalCode",
                table: "PlatformOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToRegion",
                table: "PlatformOrders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippedAt",
                table: "PlatformOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeeBreakdownJson",
                table: "PlatformOrderLines",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetSales",
                table: "PlatformOrderLines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFees",
                table: "PlatformOrderLines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawJson",
                table: "PlatformOrderLines",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerShippingCost",
                table: "PlatformOrderLines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingCharged",
                table: "PlatformOrderLines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxCollected",
                table: "PlatformOrderLines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSales",
                table: "PlatformOrderLines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            // Forward data repair: remove stale marketplace order lines whose sale was moved elsewhere, then remove
            // non-storefront orders that are left with no sales and no lines. Storefront checkout records are untouched.
            migrationBuilder.Sql(@"
                DELETE FROM ""OrderLines"" line
                USING ""Orders"" orders
                WHERE line.""OrderId"" = orders.""Id""
                  AND orders.""Channel"" <> 'Storefront'
                  AND NOT EXISTS (
                      SELECT 1 FROM ""Sales"" sale
                      WHERE sale.""OrderId"" = line.""OrderId""
                        AND sale.""InventoryItemId"" = line.""InventoryItemId"");

                DELETE FROM ""Orders"" orders
                WHERE orders.""Channel"" <> 'Storefront'
                  AND NOT EXISTS (SELECT 1 FROM ""Sales"" sale WHERE sale.""OrderId"" = orders.""Id"")
                  AND NOT EXISTS (SELECT 1 FROM ""OrderLines"" line WHERE line.""OrderId"" = orders.""Id"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerEmail",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "BuyerNote",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToCity",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToCountry",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToLine1",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToLine2",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToName",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToPhone",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToPostalCode",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShipToRegion",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "ShippedAt",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "FeeBreakdownJson",
                table: "PlatformOrderLines");

            migrationBuilder.DropColumn(
                name: "NetSales",
                table: "PlatformOrderLines");

            migrationBuilder.DropColumn(
                name: "PlatformFees",
                table: "PlatformOrderLines");

            migrationBuilder.DropColumn(
                name: "RawJson",
                table: "PlatformOrderLines");

            migrationBuilder.DropColumn(
                name: "SellerShippingCost",
                table: "PlatformOrderLines");

            migrationBuilder.DropColumn(
                name: "ShippingCharged",
                table: "PlatformOrderLines");

            migrationBuilder.DropColumn(
                name: "TaxCollected",
                table: "PlatformOrderLines");

            migrationBuilder.DropColumn(
                name: "TotalSales",
                table: "PlatformOrderLines");
        }
    }
}
