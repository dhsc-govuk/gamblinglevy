using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6031_ActivityRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "varchar(16)", nullable: false),
                    is_lead = table.Column<bool>(type: "boolean", nullable: false),
                    lead_org_id = table.Column<Guid>(type: "uuid", nullable: true),
                    custom_category_text = table.Column<string>(type: "varchar(255)", nullable: true),
                    custom_task_text = table.Column<string>(type: "varchar(255)", nullable: true),
                    is_flagged_for_review = table.Column<bool>(type: "boolean", nullable: false),
                    is_visible_in_directory = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_key_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_result_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_custom = table.Column<bool>(type: "boolean", nullable: false),
                    custom_kr_text = table.Column<string>(type: "varchar(1024)", nullable: true),
                    status = table.Column<string>(type: "varchar(16)", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    withdrawal_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_key_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_name = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    sequence_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sequence_order = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "key_result_eligibilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_type = table.Column<string>(type: "varchar(16)", nullable: false),
                    region = table.Column<string>(type: "varchar(64)", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_result_eligibilities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "key_result_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_result_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "key_result_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    guidance_html = table.Column<string>(type: "text", nullable: true),
                    metric_type = table.Column<string>(type: "varchar(16)", nullable: false),
                    unit_label = table.Column<string>(type: "varchar(64)", nullable: true),
                    change_summary = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_result_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "key_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    guidance_html = table.Column<string>(type: "text", nullable: true),
                    metric_type = table.Column<string>(type: "varchar(16)", nullable: false),
                    unit_label = table.Column<string>(type: "varchar(64)", nullable: true),
                    is_standard = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sequence_order = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    archived_by = table.Column<Guid>(type: "uuid", nullable: true),
                    archive_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_results", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_category_id",
                table: "activities",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_activities_organisation_id",
                table: "activities",
                column: "organisation_id");

            migrationBuilder.CreateIndex(
                name: "ix_activities_organisation_id_status",
                table: "activities",
                columns: new[] { "organisation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_task_id",
                table: "activities",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_key_results_activity_id",
                table: "activity_key_results",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_key_results_activity_id_key_result_id",
                table: "activity_key_results",
                columns: new[] { "activity_id", "key_result_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_activity_tasks_category_id",
                table: "activity_tasks",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_is_active",
                table: "categories",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_key_result_eligibilities_key_result_id",
                table: "key_result_eligibilities",
                column: "key_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_key_result_eligibilities_key_result_id_org_type_region",
                table: "key_result_eligibilities",
                columns: new[] { "key_result_id", "org_type", "region" });

            migrationBuilder.CreateIndex(
                name: "ix_key_result_tasks_key_result_id_task_id",
                table: "key_result_tasks",
                columns: new[] { "key_result_id", "task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_key_result_tasks_task_id",
                table: "key_result_tasks",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_key_result_versions_key_result_id_version_number",
                table: "key_result_versions",
                columns: new[] { "key_result_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_key_results_is_active",
                table: "key_results",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropTable(
                name: "activity_key_results");

            migrationBuilder.DropTable(
                name: "activity_tasks");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "key_result_eligibilities");

            migrationBuilder.DropTable(
                name: "key_result_tasks");

            migrationBuilder.DropTable(
                name: "key_result_versions");

            migrationBuilder.DropTable(
                name: "key_results");
        }
    }
}