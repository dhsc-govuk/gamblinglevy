using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6444_FormDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_answers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    label = table.Column<string>(type: "varchar(500)", nullable: false),
                    hint = table.Column<string>(type: "text", nullable: true),
                    help_text = table.Column<string>(type: "text", nullable: true),
                    help_text_summary = table.Column<string>(type: "varchar(500)", nullable: true),
                    question_type = table.Column<string>(type: "varchar(16)", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    conditional_question_code = table.Column<string>(type: "varchar(100)", nullable: true),
                    conditional_value = table.Column<string>(type: "varchar(255)", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_sections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_question_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "varchar(255)", nullable: false),
                    label = table.Column<string>(type: "varchar(500)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_question_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_question_options_form_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "form_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_answers_activity_id",
                table: "activity_answers",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_answers_activity_id_question_code",
                table: "activity_answers",
                columns: new[] { "activity_id", "question_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_question_options_question_id",
                table: "form_question_options",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_questions_code",
                table: "form_questions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_questions_section_id_display_order",
                table: "form_questions",
                columns: new[] { "section_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_form_sections_display_order",
                table: "form_sections",
                column: "display_order");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_answers");

            migrationBuilder.DropTable(
                name: "form_question_options");

            migrationBuilder.DropTable(
                name: "form_sections");

            migrationBuilder.DropTable(
                name: "form_questions");
        }
    }
}