using System;
using System.Collections.Generic;
using Domain.KeyResults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6611_KeyResultProtocolAndSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "key_result_code",
                table: "activity_expected_results",
                type: "varchar(100)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "key_result_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    label = table.Column<string>(type: "varchar(255)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_result_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "key_result_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_result_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    status_scope = table.Column<string>(type: "varchar(32)", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    hint = table.Column<string>(type: "text", nullable: true),
                    field_type = table.Column<string>(type: "varchar(32)", nullable: false),
                    is_required_on_complete = table.Column<bool>(type: "boolean", nullable: false),
                    min_value = table.Column<decimal>(type: "numeric", nullable: true),
                    max_value = table.Column<decimal>(type: "numeric", nullable: true),
                    options = table.Column<IReadOnlyList<KeyResultQuestionOption>>(type: "jsonb", nullable: true),
                    prefill_source = table.Column<string>(type: "varchar(64)", nullable: true),
                    conditional_question_code = table.Column<string>(type: "varchar(100)", nullable: true),
                    conditional_value = table.Column<string>(type: "varchar(255)", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_key_result_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submission_activity_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_activity_answers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submission_activity_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "varchar(32)", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_activity_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activities_confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activities_confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    activities_review_required = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submission_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    status = table.Column<string>(type: "varchar(32)", nullable: false),
                    review_required = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_sections_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_key_result_definitions_activity_task_id_code",
                table: "key_result_definitions",
                columns: new[] { "activity_task_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_key_result_questions_activity_task_id",
                table: "key_result_questions",
                column: "activity_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_key_result_questions_activity_task_id_code_status_scope",
                table: "key_result_questions",
                columns: new[] { "activity_task_id", "code", "status_scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_activity_answers_submission_id_activity_id",
                table: "submission_activity_answers",
                columns: new[] { "submission_id", "activity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_activity_answers_submission_id_activity_id_quest",
                table: "submission_activity_answers",
                columns: new[] { "submission_id", "activity_id", "question_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_activity_reports_submission_id_activity_id",
                table: "submission_activity_reports",
                columns: new[] { "submission_id", "activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_sections_submission_id_module_code",
                table: "submission_sections",
                columns: new[] { "submission_id", "module_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_collection_id_organisation_id",
                table: "submissions",
                columns: new[] { "collection_id", "organisation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "key_result_definitions");

            migrationBuilder.DropTable(
                name: "key_result_questions");

            migrationBuilder.DropTable(
                name: "submission_activity_answers");

            migrationBuilder.DropTable(
                name: "submission_activity_reports");

            migrationBuilder.DropTable(
                name: "submission_sections");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropColumn(
                name: "key_result_code",
                table: "activity_expected_results");
        }
    }
}