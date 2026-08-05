using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_DataCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "varchar(32)", nullable: false),
                    financial_year = table.Column<string>(type: "varchar(16)", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: true),
                    reporting_period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reporting_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    window_same_as_reporting_period = table.Column<bool>(type: "boolean", nullable: false),
                    collection_open_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submission_deadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_submitted_by_all_las = table.Column<bool>(type: "boolean", nullable: false),
                    is_draft = table.Column<bool>(type: "boolean", nullable: false),
                    is_manually_closed = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_modules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "varchar(32)", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_modules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collection_forms",
                columns: table => new
                {
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_module_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_forms", x => new { x.collection_id, x.form_module_id });
                    table.ForeignKey(
                        name: "fk_collection_forms_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_local_authorities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_authority_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_local_authorities", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_local_authorities_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offset_type = table.Column<string>(type: "varchar(32)", nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_reminders_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dc_form_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    label = table.Column<string>(type: "varchar(500)", nullable: false),
                    question_type = table.Column<string>(type: "varchar(32)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    hint = table.Column<string>(type: "text", nullable: true),
                    help_text = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dc_form_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_dc_form_questions_form_modules_form_module_id",
                        column: x => x.form_module_id,
                        principalTable: "form_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dc_form_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    help_text = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dc_form_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_dc_form_sections_form_modules_form_module_id",
                        column: x => x.form_module_id,
                        principalTable: "form_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dc_form_question_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "varchar(255)", nullable: false),
                    label = table.Column<string>(type: "varchar(500)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dc_form_question_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_dc_form_question_options_dc_form_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "dc_form_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_local_authorities_collection_id_local_authority_",
                table: "collection_local_authorities",
                columns: new[] { "collection_id", "local_authority_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_reminders_collection_id",
                table: "collection_reminders",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_collections_deleted_at",
                table: "collections",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_collections_name",
                table: "collections",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dc_form_question_options_question_id",
                table: "dc_form_question_options",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_dc_form_questions_form_module_id_section_id",
                table: "dc_form_questions",
                columns: new[] { "form_module_id", "section_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dc_form_sections_form_module_id_section_number",
                table: "dc_form_sections",
                columns: new[] { "form_module_id", "section_number" });

            migrationBuilder.CreateIndex(
                name: "ix_form_modules_code",
                table: "form_modules",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_forms");

            migrationBuilder.DropTable(
                name: "collection_local_authorities");

            migrationBuilder.DropTable(
                name: "collection_reminders");

            migrationBuilder.DropTable(
                name: "dc_form_question_options");

            migrationBuilder.DropTable(
                name: "dc_form_sections");

            migrationBuilder.DropTable(
                name: "collections");

            migrationBuilder.DropTable(
                name: "dc_form_questions");

            migrationBuilder.DropTable(
                name: "form_modules");
        }
    }
}