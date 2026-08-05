using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_ActivityRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_category_text",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "is_lead",
                table: "activities");

            migrationBuilder.RenameColumn(
                name: "lead_org_id",
                table: "activities",
                newName: "deleted_by");

            migrationBuilder.RenameColumn(
                name: "is_visible_in_directory",
                table: "activities",
                newName: "is_complete");

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                table: "form_sections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "activities",
                type: "varchar(255)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "activities",
                type: "varchar(16)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(16)");

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "start_month",
                table: "activities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "start_year",
                table: "activities",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "activity_expected_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_expected_results", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_organisation_id_deleted_at",
                table: "activities",
                columns: new[] { "organisation_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_expected_results_activity_id",
                table: "activity_expected_results",
                column: "activity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_expected_results");

            migrationBuilder.DropIndex(
                name: "ix_activities_organisation_id_deleted_at",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "form_sections");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "start_month",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "start_year",
                table: "activities");

            migrationBuilder.RenameColumn(
                name: "is_complete",
                table: "activities",
                newName: "is_visible_in_directory");

            migrationBuilder.RenameColumn(
                name: "deleted_by",
                table: "activities",
                newName: "lead_org_id");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "activities",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "activities",
                type: "varchar(16)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_category_text",
                table: "activities",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_lead",
                table: "activities",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}