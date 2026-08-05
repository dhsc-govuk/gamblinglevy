using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_7238_MfaSessionLastActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_activity_at",
                table: "mfa_sessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            // Sessions that predate this column have no activity history, and the column default would hand
            // every one of them a fresh idle window at deploy — exactly the long-lived session NR-4099 found.
            // created_at is the last point we can actually evidence the session was in use, so anything
            // already older than the idle window is retired on its next request.
            migrationBuilder.Sql("UPDATE mfa_sessions SET last_activity_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "mfa_sessions");
        }
    }
}