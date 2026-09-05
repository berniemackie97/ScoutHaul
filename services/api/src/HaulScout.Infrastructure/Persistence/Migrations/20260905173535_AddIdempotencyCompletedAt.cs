using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "IdempotencyRecords",
                type: "timestamp with time zone",
                nullable: true);

            // Every row written under the old scheme was recorded after its work had already
            // succeeded, so all of them are complete. Left null they would read as "a request with
            // this key is running right now" and answer a replay with 409 until they expired.
            migrationBuilder.Sql(
                """UPDATE "IdempotencyRecords" SET "CompletedAt" = now() WHERE "CompletedAt" IS NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "IdempotencyRecords");
        }
    }
}
