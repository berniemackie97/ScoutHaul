using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// "Other" and its cousins were stored as if they were a brand, colour, condition or size. They carry no
    /// information, so they are cleared; the editor now shows those fields empty and nothing writes them again.
    /// </summary>
    public partial class ClearPlaceholderAttributeValues : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "InventoryItems"
                SET "Brand" = NULL
                WHERE lower(btrim("Brand")) IN ('other', 'none', 'n/a', 'na', 'unknown', 'unbranded', 'no brand', 'not specified', '-');

                UPDATE "InventoryItems"
                SET "Model" = NULL
                WHERE lower(btrim("Model")) IN ('other', 'none', 'n/a', 'na', 'unknown', 'not specified', '-');

                UPDATE "InventoryItems" SET "Color" = NULL WHERE lower(btrim("Color")) = 'other';
                UPDATE "InventoryItems" SET "Condition" = NULL WHERE lower(btrim("Condition")) = 'other';

                UPDATE "InventoryItems"
                SET "SizeSystem" = NULL, "SizeValue" = NULL
                WHERE lower(btrim("SizeSystem")) = 'other' OR lower(btrim("SizeValue")) = 'other';
                """);
        }

        // The cleared words were placeholders, not data; there is nothing to put back.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
