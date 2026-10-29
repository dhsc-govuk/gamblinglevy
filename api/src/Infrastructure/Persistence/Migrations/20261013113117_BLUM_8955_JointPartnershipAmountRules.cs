using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    // BLUM-8955: the joint partnership's "How much have you committed…" amount (JP_COMMITTED) accepted 0, and its
    // messages quoted the whole question where the content spreadsheet names the field "Amount agreed".
    //
    // Three nullable columns carry what a Currency question had no place for: its minimum, and the two forms of the
    // protocol's name for the field (FormQuestion.MinValue / ErrorSubject / ErrorSubjectInline). They are seed-owned,
    // like the BLUM-7620 protocol-copy columns, and null on every other question, which then behaves as before.
    //
    // SeedFormDesigner holds the new values, but it only fills an empty schema, so this migration brings an
    // already-seeded database into line. It only updates the one row, keyed on its code (unique and immutable):
    //   • the rules are written only where all three are still unset, so re-running it does nothing;
    //   • the "empty on submit" message is matched on the OLD text, so a database that no longer holds the seeded
    //     wording is left alone.
    //
    // Saved answers are not touched. An amount of 0, or one saved while a misplaced comma was still stripped
    // ("5,10"), stays as the LA typed it and is refused the next time that record is edited.
    /// <inheritdoc />
    public partial class BLUM_8955_JointPartnershipAmountRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error_subject",
                table: "form_questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_subject_inline",
                table: "form_questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "min_value",
                table: "form_questions",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE form_questions
                SET min_value = 1,
                    error_subject = 'Amount agreed',
                    error_subject_inline = 'the amount you have agreed',
                    updated_at = now()
                WHERE code = 'JP_COMMITTED'
                  AND min_value IS NULL
                  AND error_subject IS NULL
                  AND error_subject_inline IS NULL;

                UPDATE form_questions
                SET error_message = 'Enter the amount you have agreed', updated_at = now()
                WHERE code = 'JP_COMMITTED'
                  AND error_message = 'Enter the amount you have committed to this joint partnership';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE form_questions
                SET error_message = 'Enter the amount you have committed to this joint partnership', updated_at = now()
                WHERE code = 'JP_COMMITTED'
                  AND error_message = 'Enter the amount you have agreed';
                """);

            migrationBuilder.DropColumn(
                name: "error_subject",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "error_subject_inline",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "min_value",
                table: "form_questions");
        }
    }
}