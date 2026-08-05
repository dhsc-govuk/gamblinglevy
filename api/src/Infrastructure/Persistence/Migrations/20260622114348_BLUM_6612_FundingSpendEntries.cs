using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6612_FundingSpendEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "submission_spend_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "varchar(16)", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_spend_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_submission_spend_entries_submission_id_activity_id_kind_qua",
                table: "submission_spend_entries",
                columns: new[] { "submission_id", "activity_id", "kind", "quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_spend_entries_submission_id_kind",
                table: "submission_spend_entries",
                columns: new[] { "submission_id", "kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submission_spend_entries");
        }
    }
}