using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScoutHaul.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationInviteCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationInviteCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CodePrefix = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInviteCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationInviteCodes_OrganizationRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "OrganizationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationInviteCodes_OrganizationTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "OrganizationTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationInviteCodes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationInviteCodes_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInviteCodes_CodeHash",
                table: "OrganizationInviteCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInviteCodes_CreatedByUserId",
                table: "OrganizationInviteCodes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInviteCodes_OrganizationId_IsActive",
                table: "OrganizationInviteCodes",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInviteCodes_RoleId",
                table: "OrganizationInviteCodes",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInviteCodes_TeamId",
                table: "OrganizationInviteCodes",
                column: "TeamId");

            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                WITH missing_owner_roles AS (
                    SELECT o."Id" AS organization_id, gen_random_uuid() AS role_id
                    FROM "Organizations" o
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM "OrganizationRoles" r
                        WHERE r."OrganizationId" = o."Id"
                          AND r."IsOwnerRole" = TRUE
                    )
                )
                INSERT INTO "OrganizationRoles" (
                    "Id", "OrganizationId", "Name", "Description", "IsSystem", "IsOwnerRole", "CreatedAt", "UpdatedAt"
                )
                SELECT
                    role_id,
                    organization_id,
                    'Owner',
                    'Full workspace control, including owners, billing, roles, teams, and business data.',
                    TRUE,
                    TRUE,
                    now(),
                    now()
                FROM missing_owner_roles;

                INSERT INTO "OrganizationRolePermissions" (
                    "Id", "RoleId", "Permission", "CreatedAt", "UpdatedAt"
                )
                SELECT gen_random_uuid(), r."Id", p.permission, now(), now()
                FROM "OrganizationRoles" r
                CROSS JOIN (VALUES
                    ('organization.manage'),
                    ('organization.owners.manage'),
                    ('organization.roles.manage'),
                    ('organization.teams.manage'),
                    ('organization.members.manage'),
                    ('inventory.manage'),
                    ('routes.manage'),
                    ('budgets.manage'),
                    ('listings.manage'),
                    ('tasks.manage'),
                    ('reports.view')
                ) AS p(permission)
                WHERE r."IsOwnerRole" = TRUE
                ON CONFLICT ("RoleId", "Permission") DO NOTHING;

                UPDATE "OrganizationMembers" m
                SET "RoleId" = r."Id",
                    "UpdatedAt" = now()
                FROM "OrganizationRoles" r
                WHERE m."OrganizationId" = r."OrganizationId"
                  AND r."IsOwnerRole" = TRUE
                  AND m."Role" = 'Owner'
                  AND m."RoleId" IS NULL;

                WITH role_name_candidates AS (
                    SELECT o."Id" AS organization_id, candidate.name, candidate.priority
                    FROM "Organizations" o
                    CROSS JOIN (VALUES
                        ('Operator', 1),
                        ('Team Operator', 2),
                        ('Workspace Operator', 3)
                    ) AS candidate(name, priority)
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM "OrganizationRoles" existing_system
                        WHERE existing_system."OrganizationId" = o."Id"
                          AND existing_system."IsSystem" = TRUE
                          AND existing_system."IsOwnerRole" = FALSE
                    )
                      AND NOT EXISTS (
                        SELECT 1
                        FROM "OrganizationRoles" conflicting_name
                        WHERE conflicting_name."OrganizationId" = o."Id"
                          AND conflicting_name."Name" = candidate.name
                    )
                ),
                selected_operator_roles AS (
                    SELECT DISTINCT ON (organization_id)
                        organization_id,
                        name,
                        gen_random_uuid() AS role_id
                    FROM role_name_candidates
                    ORDER BY organization_id, priority
                )
                INSERT INTO "OrganizationRoles" (
                    "Id", "OrganizationId", "Name", "Description", "IsSystem", "IsOwnerRole", "CreatedAt", "UpdatedAt"
                )
                SELECT
                    role_id,
                    organization_id,
                    name,
                    'Can run day-to-day sourcing, inventory, listings, budgets, tasks, and reports without owner administration.',
                    TRUE,
                    FALSE,
                    now(),
                    now()
                FROM selected_operator_roles;

                INSERT INTO "OrganizationRolePermissions" (
                    "Id", "RoleId", "Permission", "CreatedAt", "UpdatedAt"
                )
                SELECT gen_random_uuid(), r."Id", p.permission, now(), now()
                FROM "OrganizationRoles" r
                CROSS JOIN (VALUES
                    ('inventory.manage'),
                    ('routes.manage'),
                    ('budgets.manage'),
                    ('listings.manage'),
                    ('tasks.manage'),
                    ('reports.view')
                ) AS p(permission)
                WHERE r."IsSystem" = TRUE
                  AND r."IsOwnerRole" = FALSE
                ON CONFLICT ("RoleId", "Permission") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationInviteCodes");
        }
    }
}
