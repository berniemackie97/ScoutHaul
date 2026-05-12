using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoutHaul.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SourcingRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "SourcingTrips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlannedEndAt",
                table: "SourcingTrips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlannedStartAt",
                table: "SourcingTrips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RouteDate",
                table: "SourcingTrips",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteType",
                table: "SourcingTrips",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SourcingTrips",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<Guid>(
                name: "SourcingTripStopId",
                table: "InventoryItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourcingTripStopId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SourcingTripStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Url = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PlannedOrder = table.Column<int>(type: "integer", nullable: false),
                    PlannedArrivalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IsTimeSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    TimeWindowStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeWindowEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanBeSkipped = table.Column<bool>(type: "boolean", nullable: false),
                    BudgetCap = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActualArrivalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualDepartureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingTripStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingTripStops_SourcingTrips_SourcingTripId",
                        column: x => x.SourcingTripId,
                        principalTable: "SourcingTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourcingStopChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcingTripStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingStopChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingStopChecklistItems_SourcingTripStops_SourcingTripSt~",
                        column: x => x.SourcingTripStopId,
                        principalTable: "SourcingTripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourcingTrips_AssignedToUserId",
                table: "SourcingTrips",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingTrips_OrganizationId_RouteDate",
                table: "SourcingTrips",
                columns: new[] { "OrganizationId", "RouteDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SourcingTrips_OrganizationId_Status",
                table: "SourcingTrips",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_SourcingTripStopId",
                table: "InventoryItems",
                column: "SourcingTripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SourcingTripStopId",
                table: "Expenses",
                column: "SourcingTripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingStopChecklistItems_OrganizationId_SourcingTripStopI~",
                table: "SourcingStopChecklistItems",
                columns: new[] { "OrganizationId", "SourcingTripStopId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SourcingStopChecklistItems_SourcingTripStopId",
                table: "SourcingStopChecklistItems",
                column: "SourcingTripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingTripStops_OrganizationId_SourcingTripId_PlannedOrder",
                table: "SourcingTripStops",
                columns: new[] { "OrganizationId", "SourcingTripId", "PlannedOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SourcingTripStops_OrganizationId_Status",
                table: "SourcingTripStops",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SourcingTripStops_SourcingTripId",
                table: "SourcingTripStops",
                column: "SourcingTripId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_SourcingTripStops_SourcingTripStopId",
                table: "Expenses",
                column: "SourcingTripStopId",
                principalTable: "SourcingTripStops",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryItems_SourcingTripStops_SourcingTripStopId",
                table: "InventoryItems",
                column: "SourcingTripStopId",
                principalTable: "SourcingTripStops",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SourcingTrips_Users_AssignedToUserId",
                table: "SourcingTrips",
                column: "AssignedToUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_SourcingTripStops_SourcingTripStopId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryItems_SourcingTripStops_SourcingTripStopId",
                table: "InventoryItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SourcingTrips_Users_AssignedToUserId",
                table: "SourcingTrips");

            migrationBuilder.DropTable(
                name: "SourcingStopChecklistItems");

            migrationBuilder.DropTable(
                name: "SourcingTripStops");

            migrationBuilder.DropIndex(
                name: "IX_SourcingTrips_AssignedToUserId",
                table: "SourcingTrips");

            migrationBuilder.DropIndex(
                name: "IX_SourcingTrips_OrganizationId_RouteDate",
                table: "SourcingTrips");

            migrationBuilder.DropIndex(
                name: "IX_SourcingTrips_OrganizationId_Status",
                table: "SourcingTrips");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_SourcingTripStopId",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_SourcingTripStopId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "SourcingTrips");

            migrationBuilder.DropColumn(
                name: "PlannedEndAt",
                table: "SourcingTrips");

            migrationBuilder.DropColumn(
                name: "PlannedStartAt",
                table: "SourcingTrips");

            migrationBuilder.DropColumn(
                name: "RouteDate",
                table: "SourcingTrips");

            migrationBuilder.DropColumn(
                name: "RouteType",
                table: "SourcingTrips");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SourcingTrips");

            migrationBuilder.DropColumn(
                name: "SourcingTripStopId",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "SourcingTripStopId",
                table: "Expenses");
        }
    }
}
