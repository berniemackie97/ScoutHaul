using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(HaulScoutDbContext))]
    [Migration("20260702044000_AddPurchasingBundlesAndFeeProfiles")]
    public partial class AddPurchasingBundlesAndFeeProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBundle",
                table: "InventoryItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionLotId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcquisitionLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    SourceLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcingTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcingTripStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurchaseSubtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ShippingCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TaxCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    FeeCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OtherCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AllocationMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcquisitionLots_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AcquisitionLots_SourceLocations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "SourceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AcquisitionLots_SourcingTripStops_SourcingTripStopId",
                        column: x => x.SourcingTripStopId,
                        principalTable: "SourcingTripStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AcquisitionLots_SourcingTrips_SourcingTripId",
                        column: x => x.SourcingTripId,
                        principalTable: "SourcingTrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AcquisitionLotItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocatedCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AllocationWeight = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionLotItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcquisitionLotItems_AcquisitionLots_AcquisitionLotId",
                        column: x => x.AcquisitionLotId,
                        principalTable: "AcquisitionLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AcquisitionLotItems_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBundleComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleInventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentInventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CostSnapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TitleSnapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBundleComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryBundleComponents_InventoryItems_BundleInventoryItemId",
                        column: x => x.BundleInventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryBundleComponents_InventoryItems_ComponentInventoryItemId",
                        column: x => x.ComponentInventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_OrganizationId_IsBundle",
                table: "InventoryItems",
                columns: new[] { "OrganizationId", "IsBundle" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AcquisitionLotId",
                table: "Expenses",
                column: "AcquisitionLotId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_AcquisitionLotId",
                table: "Expenses",
                columns: new[] { "OrganizationId", "AcquisitionLotId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLotItems_AcquisitionLotId",
                table: "AcquisitionLotItems",
                column: "AcquisitionLotId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLotItems_InventoryItemId",
                table: "AcquisitionLotItems",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLotItems_OrganizationId_AcquisitionLotId",
                table: "AcquisitionLotItems",
                columns: new[] { "OrganizationId", "AcquisitionLotId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLotItems_OrganizationId_InventoryItemId",
                table: "AcquisitionLotItems",
                columns: new[] { "OrganizationId", "InventoryItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLots_OrganizationId_PurchasedAt",
                table: "AcquisitionLots",
                columns: new[] { "OrganizationId", "PurchasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLots_OrganizationId_SupplierName",
                table: "AcquisitionLots",
                columns: new[] { "OrganizationId", "SupplierName" });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLots_SourceLocationId",
                table: "AcquisitionLots",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLots_SourcingTripId",
                table: "AcquisitionLots",
                column: "SourcingTripId");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionLots_SourcingTripStopId",
                table: "AcquisitionLots",
                column: "SourcingTripStopId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBundleComponents_BundleInventoryItemId_ComponentInventoryItemId",
                table: "InventoryBundleComponents",
                columns: new[] { "BundleInventoryItemId", "ComponentInventoryItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBundleComponents_ComponentInventoryItemId",
                table: "InventoryBundleComponents",
                column: "ComponentInventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBundleComponents_OrganizationId_BundleInventoryItemId",
                table: "InventoryBundleComponents",
                columns: new[] { "OrganizationId", "BundleInventoryItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBundleComponents_OrganizationId_ComponentInventoryItemId",
                table: "InventoryBundleComponents",
                columns: new[] { "OrganizationId", "ComponentInventoryItemId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_AcquisitionLots_AcquisitionLotId",
                table: "Expenses",
                column: "AcquisitionLotId",
                principalTable: "AcquisitionLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_AcquisitionLots_AcquisitionLotId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "AcquisitionLotItems");

            migrationBuilder.DropTable(
                name: "InventoryBundleComponents");

            migrationBuilder.DropTable(
                name: "AcquisitionLots");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_OrganizationId_IsBundle",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_AcquisitionLotId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_OrganizationId_AcquisitionLotId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "IsBundle",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "AcquisitionLotId",
                table: "Expenses");
        }
    }
}
