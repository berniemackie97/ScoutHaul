using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBundledSaleGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupOrderReference",
                table: "Sales",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleGroupId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OrganizationId_SaleGroupId",
                table: "Sales",
                columns: new[] { "OrganizationId", "SaleGroupId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_OrganizationId_SaleGroupId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "GroupOrderReference",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SaleGroupId",
                table: "Sales");
        }
    }
}
