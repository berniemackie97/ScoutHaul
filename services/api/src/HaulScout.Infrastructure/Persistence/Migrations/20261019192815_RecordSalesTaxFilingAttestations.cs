using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordSalesTaxFilingAttestations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AttestedDirectSalesRecorded",
                table: "SalesTaxReturns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AttestedUseTaxReviewed",
                table: "SalesTaxReturns",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttestedDirectSalesRecorded",
                table: "SalesTaxReturns");

            migrationBuilder.DropColumn(
                name: "AttestedUseTaxReviewed",
                table: "SalesTaxReturns");
        }
    }
}
