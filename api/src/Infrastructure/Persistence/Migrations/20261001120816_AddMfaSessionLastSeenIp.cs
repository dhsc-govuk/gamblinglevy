using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaSessionLastSeenIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_seen_ip_address",
                table: "mfa_sessions",
                type: "varchar(45)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_seen_ip_address_hash",
                table: "mfa_sessions",
                type: "varchar(64)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_ip_address",
                table: "mfa_sessions");

            migrationBuilder.DropColumn(
                name: "last_seen_ip_address_hash",
                table: "mfa_sessions");
        }
    }
}