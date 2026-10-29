using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    // The BLUM_XXXX in this name is a placeholder that was never replaced, and it stays for good. EF knows a
    // migration only by the [Migration] id in the Designer file, and every deployed database has
    // "20260922093051_BLUM_XXXX_UserPasswordLoginEnabled" in __EFMigrationsHistory. A different id would read as
    // pending there, re-run AddColumn, fail with 42701 and crash-loop boot. The class and file keep the id's name so
    // the three cannot disagree. MigrationConventionsTests allows this one id and rejects any new placeholder
    // (BLUM-8749).
    /// <inheritdoc />
    public partial class BLUM_XXXX_UserPasswordLoginEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "password_login_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_login_enabled",
                table: "users");
        }
    }
}