using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_6613_SubmissionFeedbackAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "submission_feedback_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_code = table.Column<string>(type: "varchar(100)", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_feedback_answers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_submission_feedback_answers_submission_id",
                table: "submission_feedback_answers",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_feedback_answers_submission_id_question_code",
                table: "submission_feedback_answers",
                columns: new[] { "submission_id", "question_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submission_feedback_answers");
        }
    }
}