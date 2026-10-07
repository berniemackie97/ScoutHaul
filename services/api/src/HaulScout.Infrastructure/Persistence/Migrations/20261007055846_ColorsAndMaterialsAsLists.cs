using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// An item's colour was one text column, and listings synced from Poshmark had already put two colours in it
    /// ("Gray, Green"), which the editor could neither show nor keep. Colours become a list, filled from that text in
    /// the order written; a material written as one phrase ("Silk, Cotton") becomes a list the same way. The API
    /// holds both to their vocabularies on every save from here on.
    /// </summary>
    public partial class ColorsAndMaterialsAsLists : Migration
    {
        private const string ListSeparator = @"\s*[,/&+;|]\s*|\s+and\s+";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Colors",
                table: "InventoryItems",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.Sql($$"""
                WITH vocabulary(spelling, term) AS (
                    VALUES ('black', 'Black'), ('white', 'White'), ('gray', 'Gray'), ('grey', 'Gray'), ('silver', 'Silver'),
                           ('red', 'Red'), ('orange', 'Orange'), ('yellow', 'Yellow'), ('green', 'Green'), ('blue', 'Blue'),
                           ('purple', 'Purple'), ('pink', 'Pink'), ('brown', 'Brown'), ('tan', 'Tan'), ('khaki', 'Khaki'),
                           ('beige', 'Beige'), ('cream', 'Cream'), ('off-white', 'Cream'), ('off white', 'Cream'), ('ivory', 'Cream'),
                           ('navy', 'Navy'), ('teal', 'Teal'), ('maroon', 'Maroon'), ('burgundy', 'Maroon'), ('gold', 'Gold'),
                           ('rose gold', 'Rose gold'), ('bronze', 'Bronze'), ('natural', 'Natural'), ('multicolor', 'Multicolor'),
                           ('multi', 'Multicolor'), ('patterned', 'Patterned'), ('clear', 'Clear')),
                parts AS (
                    SELECT item."Id", vocabulary.term, min(split.position) AS position
                    FROM "InventoryItems" item
                    CROSS JOIN LATERAL regexp_split_to_table(item."Color", '{{ListSeparator}}', 'i') WITH ORDINALITY AS split(part, position)
                    JOIN vocabulary ON vocabulary.spelling = lower(btrim(split.part))
                    GROUP BY item."Id", vocabulary.term)
                UPDATE "InventoryItems" item
                SET "Colors" = (SELECT (array_agg(parts.term ORDER BY parts.position))[1:2] FROM parts WHERE parts."Id" = item."Id")
                WHERE item."Id" IN (SELECT "Id" FROM parts);

                UPDATE "InventoryItems"
                SET "AttributesJson" = jsonb_set(
                    "AttributesJson",
                    '{material}',
                    (SELECT coalesce(jsonb_agg(initcap(btrim(split.part)) ORDER BY split.position), '[]'::jsonb)
                     FROM regexp_split_to_table("AttributesJson" ->> 'material', '{{ListSeparator}}', 'i') WITH ORDINALITY AS split(part, position)
                     WHERE btrim(split.part) <> ''))
                WHERE jsonb_typeof("AttributesJson" -> 'material') = 'string';
                """);

            migrationBuilder.DropColumn(
                name: "Color",
                table: "InventoryItems");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "InventoryItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "InventoryItems" SET "Color" = array_to_string("Colors", ', ') WHERE cardinality("Colors") > 0;

                UPDATE "InventoryItems"
                SET "AttributesJson" = jsonb_set(
                    "AttributesJson",
                    '{material}',
                    to_jsonb((SELECT string_agg(value, ', ') FROM jsonb_array_elements_text("AttributesJson" -> 'material'))))
                WHERE jsonb_typeof("AttributesJson" -> 'material') = 'array';
                """);

            migrationBuilder.DropColumn(
                name: "Colors",
                table: "InventoryItems");
        }
    }
}
