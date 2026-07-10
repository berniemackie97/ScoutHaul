using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyOrdersAndCustomersAcrossChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_OrganizationId_Email",
                table: "Customers");

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Orders",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AddColumn<string>(
                name: "BuyerReference",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Every existing order predates multi-channel and came from the storefront. Stamp it, rather than
            // leaving an empty channel that would break the "storefront is simply channel #1" invariant.
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "Orders",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Storefront");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelAccountId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderId",
                table: "Orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderUrl",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Customers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.CreateTable(
                name: "CustomerChannelIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerChannelIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerChannelIdentities_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OrderId",
                table: "Sales",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OrganizationId_OrderId",
                table: "Sales",
                columns: new[] { "OrganizationId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ChannelAccountId",
                table: "Orders",
                column: "ChannelAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrganizationId_Channel",
                table: "Orders",
                columns: new[] { "OrganizationId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrganizationId_Channel_ExternalOrderId",
                table: "Orders",
                columns: new[] { "OrganizationId", "Channel", "ExternalOrderId" },
                unique: true,
                filter: "\"ExternalOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_OrganizationId_Email",
                table: "Customers",
                columns: new[] { "OrganizationId", "Email" },
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerChannelIdentities_CustomerId",
                table: "CustomerChannelIdentities",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerChannelIdentities_OrganizationId_Channel_ExternalUs~",
                table: "CustomerChannelIdentities",
                columns: new[] { "OrganizationId", "Channel", "ExternalUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ChannelAccounts_ChannelAccountId",
                table: "Orders",
                column: "ChannelAccountId",
                principalTable: "ChannelAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Orders_OrderId",
                table: "Sales",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill, idempotent and safe to re-run:
            // 1. Any order that somehow landed without a channel is a storefront order.
            migrationBuilder.Sql(@"UPDATE ""Orders"" SET ""Channel"" = 'Storefront' WHERE ""Channel"" IS NULL OR ""Channel"" = '';");

            // 2. Every existing customer is a storefront customer, keyed by their email. This is what makes the
            //    Customers page channel-aware for data that predates multi-channel.
            migrationBuilder.Sql(@"
                INSERT INTO ""CustomerChannelIdentities""
                    (""Id"", ""OrganizationId"", ""CustomerId"", ""Channel"", ""ExternalUserId"", ""DisplayName"", ""Email"", ""FirstSeenAt"", ""LastSeenAt"", ""CreatedAt"", ""UpdatedAt"")
                SELECT gen_random_uuid(), c.""OrganizationId"", c.""Id"", 'Storefront', c.""Email"", c.""DisplayName"", c.""Email"",
                       c.""CreatedAt"", c.""UpdatedAt"", now(), now()
                FROM ""Customers"" c
                WHERE c.""Email"" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM ""CustomerChannelIdentities"" i
                      WHERE i.""CustomerId"" = c.""Id"" AND i.""Channel"" = 'Storefront');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_ChannelAccounts_ChannelAccountId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Orders_OrderId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "CustomerChannelIdentities");

            migrationBuilder.DropIndex(
                name: "IX_Sales_OrderId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_OrganizationId_OrderId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ChannelAccountId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrganizationId_Channel",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrganizationId_Channel_ExternalOrderId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Customers_OrganizationId_Email",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BuyerReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ChannelAccountId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderUrl",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Orders",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Customers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_OrganizationId_Email",
                table: "Customers",
                columns: new[] { "OrganizationId", "Email" },
                unique: true);
        }
    }
}
