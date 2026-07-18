using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundExternalKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalRefundKey",
                table: "Refunds",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_OrganizationId_ExternalRefundKey",
                table: "Refunds",
                columns: new[] { "OrganizationId", "ExternalRefundKey" },
                unique: true,
                filter: "\"ExternalRefundKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refunds_OrganizationId_ExternalRefundKey",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "ExternalRefundKey",
                table: "Refunds");
        }
    }
}
