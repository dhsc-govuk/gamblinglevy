using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7081: the 17 July flow rework deactivated NA_SHARED, leaving Needs assessment as the only generic category
// that never asked protocol question 1.BCDEF.1.4 — Strategy and Licensing still do. Seed content lives only in
// SeedFormDesigner (migrations never INSERT or UPDATE it), and that seeder is deliberately seed-if-empty so DHSC
// admins own the schema through the Form Designer after its first run. So the fix ships as this one-shot cleanup:
// clear the form schema tables, and the seeder rebuilds the canonical set — with NA_SHARED restored — on the next
// boot. Runs exactly once per database (__EFMigrationsHistory), never on every boot.
//
// Deliberately narrow. Activities, activity_answers, activity_expected_results and submissions are all kept:
// answers resolve by their snapshotted question_code (ActivityAnswer.QuestionId is write-only — no read path joins
// it), so re-minted question ids strand nothing. Categories, activity_tasks and key_result_* are untouched. The one
// cost is any section or question a DHSC admin authored through the Form Designer before this deploy — the guard
// needs form_sections empty, and the seeder only rebuilds the canonical set (none expected at MVP; called out in
// the PR).
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721120000_BLUM_7081_ReseedActivityFormSchema")]
public partial class BLUM_7081_ReseedActivityFormSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and the seeder rebuilds the schema on the next
        // boot regardless of which direction we arrived from.
    }
}