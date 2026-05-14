using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoutHaul.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductIdentityDataSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataSource",
                table: "ProductIdentities",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataSource",
                table: "ProductIdentities");
        }
    }
}
