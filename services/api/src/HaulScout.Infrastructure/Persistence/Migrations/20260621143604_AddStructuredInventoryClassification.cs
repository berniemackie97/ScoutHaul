using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredInventoryClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthenticityStatus",
                table: "InventoryItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NotAssessed");

            migrationBuilder.AddColumn<string>(
                name: "CompletenessStatus",
                table: "InventoryItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "CountryOfOrigin",
                table: "InventoryItems",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DimensionUnit",
                table: "InventoryItems",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Inches");

            migrationBuilder.AddColumn<string>(
                name: "FunctionalStatus",
                table: "InventoryItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<decimal>(
                name: "Height",
                table: "InventoryItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Length",
                table: "InventoryItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManufactureYear",
                table: "InventoryItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManufacturerPartNumber",
                table: "InventoryItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipType",
                table: "InventoryItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Owned");

            migrationBuilder.AddColumn<string>(
                name: "PackagingStatus",
                table: "InventoryItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "InventoryItems",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Weight",
                table: "InventoryItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeightUnit",
                table: "InventoryItems",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Ounces");

            migrationBuilder.AddColumn<decimal>(
                name: "Width",
                table: "InventoryItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_OrganizationId_Brand",
                table: "InventoryItems",
                columns: new[] { "OrganizationId", "Brand" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_OrganizationId_Category",
                table: "InventoryItems",
                columns: new[] { "OrganizationId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_OrganizationId_StorageLocation",
                table: "InventoryItems",
                columns: new[] { "OrganizationId", "StorageLocation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_OrganizationId_Brand",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_OrganizationId_Category",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_OrganizationId_StorageLocation",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "AuthenticityStatus",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "CompletenessStatus",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "CountryOfOrigin",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "DimensionUnit",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "FunctionalStatus",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Length",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "ManufactureYear",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "ManufacturerPartNumber",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "OwnershipType",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "PackagingStatus",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "WeightUnit",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "InventoryItems");
        }
    }
}
