using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_7855_MaturityAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maturity_assessment_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "varchar(32)", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maturity_assessment_answers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maturity_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maturity_assessments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maturity_baseline_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    survey_year = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maturity_baseline_answers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maturity_assessment_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    status = table.Column<string>(type: "varchar(32)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maturity_assessment_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_maturity_assessment_sections_maturity_assessments_assessmen",
                        column: x => x.assessment_id,
                        principalTable: "maturity_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maturity_assessment_answers_assessment_id",
                table: "maturity_assessment_answers",
                column: "assessment_id");

            migrationBuilder.CreateIndex(
                name: "ix_maturity_assessment_answers_assessment_id_question_code",
                table: "maturity_assessment_answers",
                columns: new[] { "assessment_id", "question_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maturity_assessment_sections_assessment_id_section_code",
                table: "maturity_assessment_sections",
                columns: new[] { "assessment_id", "section_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maturity_assessments_collection_id_organisation_id",
                table: "maturity_assessments",
                columns: new[] { "collection_id", "organisation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maturity_assessments_reference_number",
                table: "maturity_assessments",
                column: "reference_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maturity_baseline_answers_organisation_id",
                table: "maturity_baseline_answers",
                column: "organisation_id");

            migrationBuilder.CreateIndex(
                name: "ix_maturity_baseline_answers_organisation_id_survey_year_quest",
                table: "maturity_baseline_answers",
                columns: new[] { "organisation_id", "survey_year", "question_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maturity_assessment_answers");

            migrationBuilder.DropTable(
                name: "maturity_assessment_sections");

            migrationBuilder.DropTable(
                name: "maturity_baseline_answers");

            migrationBuilder.DropTable(
                name: "maturity_assessments");
        }
    }
}