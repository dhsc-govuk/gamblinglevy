using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_QuarterlyReportSubmit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reference_number",
                table: "submissions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by",
                table: "submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_reference_number",
                table: "submissions",
                column: "reference_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_submissions_reference_number",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "reference_number",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "submitted_by",
                table: "submissions");
        }
    }
}