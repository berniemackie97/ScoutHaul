using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFloridaSalesTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesTaxAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxAdjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesTaxRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Jurisdiction = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CertificateNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BusinessPartnerNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DoingBusinessAs = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    County = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RegisteredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    FilesElectronically = table.Column<bool>(type: "boolean", nullable: false),
                    DonateCollectionAllowance = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryChargesOptional = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesTaxSaleDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Destination = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    County = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Exemption = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ExemptionCertificate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxSaleDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesTaxSaleDecisions_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesTaxUseEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    County = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OtherStateTaxPaid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxUseEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesTaxUseEntries_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SalesTaxUseEntries_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SalesTaxFrequencyChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Alignment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxFrequencyChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesTaxFrequencyChanges_SalesTaxRegistrations_Registration~",
                        column: x => x.RegistrationId,
                        principalTable: "SalesTaxRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesTaxReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Jurisdiction = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SupersedesReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    FiledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConfirmationNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AmountPaid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DonatedCollectionAllowance = table.Column<bool>(type: "boolean", nullable: false),
                    GrossSales = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalTaxDue = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AmountDue = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AmountDueWithReturn = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DeductionsCarriedOut = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreditsCarriedOut = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    WarningsAcknowledged = table.Column<int>(type: "integer", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FiledByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesTaxReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesTaxReturns_SalesTaxRegistrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "SalesTaxRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesTaxReturns_SalesTaxReturns_SupersedesReturnId",
                        column: x => x.SupersedesReturnId,
                        principalTable: "SalesTaxReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxAdjustments_OrganizationId_PeriodStart_PeriodEnd",
                table: "SalesTaxAdjustments",
                columns: new[] { "OrganizationId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxFrequencyChanges_RegistrationId_EffectiveFrom",
                table: "SalesTaxFrequencyChanges",
                columns: new[] { "RegistrationId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxRegistrations_OrganizationId_Jurisdiction",
                table: "SalesTaxRegistrations",
                columns: new[] { "OrganizationId", "Jurisdiction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxReturns_OrganizationId_Jurisdiction_PeriodStart_Per~",
                table: "SalesTaxReturns",
                columns: new[] { "OrganizationId", "Jurisdiction", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxReturns_RegistrationId",
                table: "SalesTaxReturns",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxReturns_SupersedesReturnId",
                table: "SalesTaxReturns",
                column: "SupersedesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxSaleDecisions_OrganizationId_SaleId",
                table: "SalesTaxSaleDecisions",
                columns: new[] { "OrganizationId", "SaleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxSaleDecisions_SaleId",
                table: "SalesTaxSaleDecisions",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxUseEntries_ExpenseId",
                table: "SalesTaxUseEntries",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxUseEntries_InventoryItemId",
                table: "SalesTaxUseEntries",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesTaxUseEntries_OrganizationId_OccurredOn",
                table: "SalesTaxUseEntries",
                columns: new[] { "OrganizationId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesTaxAdjustments");

            migrationBuilder.DropTable(
                name: "SalesTaxFrequencyChanges");

            migrationBuilder.DropTable(
                name: "SalesTaxReturns");

            migrationBuilder.DropTable(
                name: "SalesTaxSaleDecisions");

            migrationBuilder.DropTable(
                name: "SalesTaxUseEntries");

            migrationBuilder.DropTable(
                name: "SalesTaxRegistrations");
        }
    }
}
