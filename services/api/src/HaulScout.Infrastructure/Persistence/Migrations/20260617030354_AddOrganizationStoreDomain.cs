using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationStoreDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoreDomain",
                table: "Organizations",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_StoreDomain",
                table: "Organizations",
                column: "StoreDomain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_StoreDomain",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "StoreDomain",
                table: "Organizations");
        }
    }
}
