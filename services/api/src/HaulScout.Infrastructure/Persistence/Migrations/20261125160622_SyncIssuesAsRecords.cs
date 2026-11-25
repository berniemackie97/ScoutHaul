using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncIssuesAsRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "SyncRunIssues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SyncRunIssues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "SyncRunIssues",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fix",
                table: "SyncRunIssues",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "SyncRunIssues",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "SyncRunIssues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedByUserId",
                table: "SyncRunIssues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot",
                table: "SyncRunIssues",
                type: "character varying(16000)",
                maxLength: 16000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "SyncRunIssues",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Open");

            migrationBuilder.AddColumn<string>(
                name: "Step",
                table: "SyncRunIssues",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunIssues_OrganizationId_State",
                table: "SyncRunIssues",
                columns: new[] { "OrganizationId", "State" });
        
            // Issues raised before they could be closed would otherwise all open at once. Recent ones stay open;
            // older ones are history and close with that said.
            migrationBuilder.Sql("""
                UPDATE "SyncRunIssues"
                SET "State" = 'Resolved',
                    "Resolution" = 'Recorded before sync issues could be resolved.',
                    "ResolvedAt" = now()
                WHERE "CreatedAt" < now() - interval '14 days';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncRunIssues_OrganizationId_State",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "Detail",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "Fix",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "Snapshot",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "State",
                table: "SyncRunIssues");

            migrationBuilder.DropColumn(
                name: "Step",
                table: "SyncRunIssues");
        }
    }
}
