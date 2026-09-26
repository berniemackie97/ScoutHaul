using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListingSoldSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MarkedSoldAt",
                table: "PlatformListings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SoldAcknowledgedAt",
                table: "PlatformListings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarkedSoldAt",
                table: "PlatformListings");

            migrationBuilder.DropColumn(
                name: "SoldAcknowledgedAt",
                table: "PlatformListings");
        }
    }
}
