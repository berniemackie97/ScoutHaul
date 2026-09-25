using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListingPhotoSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoSyncError",
                table: "PlatformListings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoSyncedKey",
                table: "PlatformListings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhotoUrlsChangedAt",
                table: "PlatformListings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsJson",
                table: "PlatformListings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsKey",
                table: "PlatformListings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhotosSyncedAt",
                table: "PlatformListings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "InventoryPhotos",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoSourceKey",
                table: "InventoryItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhotoSyncEnabled",
                table: "ChannelAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryPhotos_InventoryItemId_SourceUrl",
                table: "InventoryPhotos",
                columns: new[] { "InventoryItemId", "SourceUrl" },
                unique: true,
                filter: "\"SourceUrl\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryPhotos_InventoryItemId_SourceUrl",
                table: "InventoryPhotos");

            migrationBuilder.DropColumn(
                name: "PhotoSyncError",
                table: "PlatformListings");

            migrationBuilder.DropColumn(
                name: "PhotoSyncedKey",
                table: "PlatformListings");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsChangedAt",
                table: "PlatformListings");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsJson",
                table: "PlatformListings");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsKey",
                table: "PlatformListings");

            migrationBuilder.DropColumn(
                name: "PhotosSyncedAt",
                table: "PlatformListings");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "InventoryPhotos");

            migrationBuilder.DropColumn(
                name: "PhotoSourceKey",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "PhotoSyncEnabled",
                table: "ChannelAccounts");
        }
    }
}
