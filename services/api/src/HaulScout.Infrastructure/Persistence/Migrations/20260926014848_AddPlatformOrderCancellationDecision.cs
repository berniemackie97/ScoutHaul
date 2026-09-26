using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOrderCancellationDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancellationDecidedAt",
                table: "PlatformOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationNote",
                table: "PlatformOrders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancellationOutcome",
                table: "PlatformOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledOnPlatformAt",
                table: "PlatformOrders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationDecidedAt",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "CancellationNote",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "CancellationOutcome",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "CancelledOnPlatformAt",
                table: "PlatformOrders");
        }
    }
}
