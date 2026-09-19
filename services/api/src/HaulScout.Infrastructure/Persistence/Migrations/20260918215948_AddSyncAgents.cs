using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Loop",
                table: "SyncRuns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScanRequestId",
                table: "SyncRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncAgentId",
                table: "SyncRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Trigger",
                table: "SyncRuns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FeesPending",
                table: "PlatformOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BoundAt",
                table: "ChannelAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BoundByUserId",
                table: "ChannelAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundMarketplaceUserId",
                table: "ChannelAccounts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundUsername",
                table: "ChannelAccounts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListingsScanEveryMinutes",
                table: "ChannelAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesScanEveryMinutes",
                table: "ChannelAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Browser = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OperatingSystem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ExtensionVersion = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PairedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PairedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenIssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreviousTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncAgents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncAgents_Users_PairedByUserId",
                        column: x => x.PairedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncLoops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LeaseAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Cursor = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncLoops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncLoops_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncPairings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncPairings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncScanRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Loop = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    From = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SyncAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncScanRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncScanRequests_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncAgentMarketplaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AccessGranted = table.Column<bool>(type: "boolean", nullable: false),
                    SignedIn = table.Column<bool>(type: "boolean", nullable: true),
                    Username = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MarketplaceUserId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PausedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PauseReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncAgentMarketplaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncAgentMarketplaces_SyncAgents_SyncAgentId",
                        column: x => x.SyncAgentId,
                        principalTable: "SyncAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_SyncAgentId",
                table: "SyncRuns",
                column: "SyncAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncAgentMarketplaces_SyncAgentId_Platform",
                table: "SyncAgentMarketplaces",
                columns: new[] { "SyncAgentId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncAgents_OrganizationId",
                table: "SyncAgents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncAgents_PairedByUserId",
                table: "SyncAgents",
                column: "PairedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncAgents_PreviousTokenHash",
                table: "SyncAgents",
                column: "PreviousTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_SyncAgents_TokenHash",
                table: "SyncAgents",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncLoops_ChannelAccountId_Kind",
                table: "SyncLoops",
                columns: new[] { "ChannelAccountId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncLoops_OrganizationId",
                table: "SyncLoops",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncPairings_CodeHash",
                table: "SyncPairings",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncPairings_OrganizationId",
                table: "SyncPairings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncScanRequests_ChannelAccountId_Loop_Status",
                table: "SyncScanRequests",
                columns: new[] { "ChannelAccountId", "Loop", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncScanRequests_OrganizationId_Status",
                table: "SyncScanRequests",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_SyncRuns_SyncAgents_SyncAgentId",
                table: "SyncRuns",
                column: "SyncAgentId",
                principalTable: "SyncAgents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncRuns_SyncAgents_SyncAgentId",
                table: "SyncRuns");

            migrationBuilder.DropTable(
                name: "SyncAgentMarketplaces");

            migrationBuilder.DropTable(
                name: "SyncLoops");

            migrationBuilder.DropTable(
                name: "SyncPairings");

            migrationBuilder.DropTable(
                name: "SyncScanRequests");

            migrationBuilder.DropTable(
                name: "SyncAgents");

            migrationBuilder.DropIndex(
                name: "IX_SyncRuns_SyncAgentId",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "Loop",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "ScanRequestId",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "SyncAgentId",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "Trigger",
                table: "SyncRuns");

            migrationBuilder.DropColumn(
                name: "FeesPending",
                table: "PlatformOrders");

            migrationBuilder.DropColumn(
                name: "BoundAt",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "BoundByUserId",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "BoundMarketplaceUserId",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "BoundUsername",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "ListingsScanEveryMinutes",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "SalesScanEveryMinutes",
                table: "ChannelAccounts");
        }
    }
}
