using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOrderLineItemIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlatformOrderLines_OrganizationId_ExternalItemId",
                table: "PlatformOrderLines",
                columns: new[] { "OrganizationId", "ExternalItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlatformOrderLines_OrganizationId_ExternalItemId",
                table: "PlatformOrderLines");
        }
    }
}
