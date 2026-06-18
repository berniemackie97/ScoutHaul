using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryDataSafetyOverhaul : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Accessories",
                table: "InventoryItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "InventoryItems",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "InventoryItems",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CleaningNotes",
                table: "InventoryItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "InventoryItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ListingNotes",
                table: "InventoryItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeasurementsJson",
                table: "InventoryItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MissingParts",
                table: "InventoryItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "InventoryItems",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeSystem",
                table: "InventoryItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeValue",
                table: "InventoryItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageLocation",
                table: "InventoryItems",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestingNotes",
                table: "InventoryItems",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsHero = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OriginalBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    ThumbnailBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    OriginalByteSize = table.Column<long>(type: "bigint", nullable: false),
                    ThumbnailByteSize = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryPhotos_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryPhotos_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssueType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Severity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryIssues_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryIssues_InventoryPhotos_InventoryPhotoId",
                        column: x => x.InventoryPhotoId,
                        principalTable: "InventoryPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InventoryIssues_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIssues_InventoryItemId",
                table: "InventoryIssues",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIssues_InventoryPhotoId",
                table: "InventoryIssues",
                column: "InventoryPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIssues_OrganizationId_InventoryItemId_IsResolved",
                table: "InventoryIssues",
                columns: new[] { "OrganizationId", "InventoryItemId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPhotos_InventoryItemId_IsHero",
                table: "InventoryPhotos",
                columns: new[] { "InventoryItemId", "IsHero" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPhotos_OrganizationId_InventoryItemId_SortOrder",
                table: "InventoryPhotos",
                columns: new[] { "OrganizationId", "InventoryItemId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryIssues");

            migrationBuilder.DropTable(
                name: "InventoryPhotos");

            migrationBuilder.DropColumn(
                name: "Accessories",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "CleaningNotes",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "ListingNotes",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "MeasurementsJson",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "MissingParts",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "SizeSystem",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "SizeValue",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "StorageLocation",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "TestingNotes",
                table: "InventoryItems");
        }
    }
}
