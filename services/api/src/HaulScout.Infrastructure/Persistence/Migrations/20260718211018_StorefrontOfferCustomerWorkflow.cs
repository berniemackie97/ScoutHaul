using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StorefrontOfferCustomerWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_OrganizationId_ExternalSubject",
                table: "Customers");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConvertedAt",
                table: "StorefrontOffers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "StorefrontOffers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "StorefrontOffers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ResponseAmount",
                table: "StorefrontOffers",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseMessage",
                table: "StorefrontOffers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorefrontOfferId",
                table: "OrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorefrontOfferId",
                table: "CartLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOffers_CustomerId",
                table: "StorefrontOffers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontOffers_OrganizationId_CustomerId",
                table: "StorefrontOffers",
                columns: new[] { "OrganizationId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrganizationId_StorefrontOfferId",
                table: "OrderLines",
                columns: new[] { "OrganizationId", "StorefrontOfferId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StorefrontOfferId",
                table: "OrderLines",
                column: "StorefrontOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_OrganizationId_ExternalSubject",
                table: "Customers",
                columns: new[] { "OrganizationId", "ExternalSubject" },
                unique: true,
                filter: "\"ExternalSubject\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CartLines_OrganizationId_StorefrontOfferId",
                table: "CartLines",
                columns: new[] { "OrganizationId", "StorefrontOfferId" });

            migrationBuilder.CreateIndex(
                name: "IX_CartLines_StorefrontOfferId",
                table: "CartLines",
                column: "StorefrontOfferId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartLines_StorefrontOffers_StorefrontOfferId",
                table: "CartLines",
                column: "StorefrontOfferId",
                principalTable: "StorefrontOffers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_StorefrontOffers_StorefrontOfferId",
                table: "OrderLines",
                column: "StorefrontOfferId",
                principalTable: "StorefrontOffers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontOffers_Customers_CustomerId",
                table: "StorefrontOffers",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartLines_StorefrontOffers_StorefrontOfferId",
                table: "CartLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_StorefrontOffers_StorefrontOfferId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontOffers_Customers_CustomerId",
                table: "StorefrontOffers");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontOffers_CustomerId",
                table: "StorefrontOffers");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontOffers_OrganizationId_CustomerId",
                table: "StorefrontOffers");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_OrganizationId_StorefrontOfferId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_StorefrontOfferId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_Customers_OrganizationId_ExternalSubject",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CartLines_OrganizationId_StorefrontOfferId",
                table: "CartLines");

            migrationBuilder.DropIndex(
                name: "IX_CartLines_StorefrontOfferId",
                table: "CartLines");

            migrationBuilder.DropColumn(
                name: "ConvertedAt",
                table: "StorefrontOffers");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "StorefrontOffers");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "StorefrontOffers");

            migrationBuilder.DropColumn(
                name: "ResponseAmount",
                table: "StorefrontOffers");

            migrationBuilder.DropColumn(
                name: "ResponseMessage",
                table: "StorefrontOffers");

            migrationBuilder.DropColumn(
                name: "StorefrontOfferId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "StorefrontOfferId",
                table: "CartLines");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_OrganizationId_ExternalSubject",
                table: "Customers",
                columns: new[] { "OrganizationId", "ExternalSubject" });
        }
    }
}
