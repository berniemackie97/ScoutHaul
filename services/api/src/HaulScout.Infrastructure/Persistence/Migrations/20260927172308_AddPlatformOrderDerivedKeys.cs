using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOrderDerivedKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KeysAreDerived",
                table: "PlatformOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Orders the Depop sales report made keys up for, before the flag existed.
            migrationBuilder.Sql("""
                UPDATE "PlatformOrders" SET "KeysAreDerived" = TRUE
                WHERE "Platform" = 'Depop' AND "ExternalOrderId" LIKE 'depop-%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeysAreDerived",
                table: "PlatformOrders");
        }
    }
}
