using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7479: "What related issues will it address?" blocked Continue on the People and issues step when left
// blank. SeedFormDesigner now seeds {EDU,HELP}_RELATED_ISSUES as isRequired: false with a GDS "(optional)"
// suffix on its label; the client shim that force-flipped the flag on any _RELATED_ISSUES code has gone too.
//
// Seed content lives only in SeedFormDesigner (migrations never INSERT or UPDATE it — see the emptied-out
// 20260716102609_BLUM_7041_RequireActivityDetailsQuestions), and that seeder is deliberately seed-if-empty so
// DHSC admins own the schema through the Form Designer after its first run. So the changed flag and label
// cannot reach an already-seeded database without clearing the guard. This is that one-shot clear; the seeder
// rebuilds the canonical schema on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Data only — no DDL, so there is no model change and no .Designer.cs.
//
// Safer than the earlier reseeds of these tables because this change only RELAXES a requirement: no existing
// activity becomes invalid, nothing is stranded, and no answer is orphaned. RELATED_ISSUES_OTHER stays
// required while its "other" reveal is showing, so a record that answered both still validates unchanged.
// Answers resolve by their snapshotted question_code (ActivityAnswer.QuestionId is write-only — no read path
// joins it), so re-minted question ids strand nothing.
//
// Blast radius is these three tables alone. The data-collection forms builder and strategic feedback live on
// form_modules/dc_form_sections/dc_form_questions, and activities, activity_answers, activity_expected_results,
// submissions, categories, activity_tasks and key_result_* are all untouched. The one cost is any section or
// question a DHSC admin authored through the Form Designer before this deploy — the guard needs form_sections
// empty, and the seeder only rebuilds the canonical set (none expected pre-go-live; called out in the PR).
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260819160000_BLUM_7479_MakeRelatedIssuesOptional")]
public partial class BLUM_7479_MakeRelatedIssuesOptional : Migration
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