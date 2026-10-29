using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_8750_AuditLogHousekeeping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "payload_version",
                table: "audit_logs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // CONCURRENTLY, outside the migration transaction: a plain CREATE INDEX blocks inserts into audit_logs
            // until it finishes, and every audited write inserts there inside its own transaction. A concurrent build
            // that fails part-way leaves an INVALID index under this name, which IF NOT EXISTS would then skip on the
            // retry, so any leftover is dropped first. Only this migration creates the index, so nothing else is lost.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_audit_logs_actor_user_id_occurred_at;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_audit_logs_actor_user_id_occurred_at ON audit_logs (actor_user_id, occurred_at);",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_audit_logs_actor_user_id_occurred_at;",
                suppressTransaction: true);

            migrationBuilder.DropColumn(
                name: "payload_version",
                table: "audit_logs");
        }
    }
}