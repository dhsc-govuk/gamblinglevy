using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7092: the 17 July flow rework gave Education and Help a category-wide "People and issues" section, so
// protocol questions 1.EF.1.5/1.6 rendered for every activity type instead of only the "Other ..." type.
// SeedFormDesigner now scopes that section to the category's "Other ..." activity task.
//
// Seed content lives only in SeedFormDesigner (migrations never INSERT or UPDATE it), and that seeder is
// deliberately seed-if-empty so DHSC admins own the schema through the Form Designer after its first run. So the
// fix ships as this one-shot cleanup: clear the form schema tables, and the seeder rebuilds the canonical set —
// with the section type-scoped — on the next boot. Runs exactly once per database (__EFMigrationsHistory), never
// on every boot.
//
// Deliberately narrow. Activities, activity_expected_results and submissions are all kept: answers resolve by their
// snapshotted question_code (ActivityAnswer.QuestionId is write-only — no read path joins it), so re-minted question
// ids strand nothing. Categories, activity_tasks and key_result_* are untouched. The one cost is any section or
// question a DHSC admin authored through the Form Designer before this deploy — the guard needs form_sections empty,
// and the seeder only rebuilds the canonical set (none expected at MVP; called out in the PR).
//
// The second statement cleans up activity_answers rows the rescope makes unreachable. Between the 17 July rework and
// this deploy the 1.EF questions were mandatory for EVERY Education/Help type, so ordinary types hold answers that
// no longer belong to any applicable section: the wizard, editor and view all filter the section out by
// activity_task_id, and UpdateActivityCommand deliberately preserves answers scoped to another type, so nothing
// would ever read or clear them again. Keyed on question_code because this migration re-mints question ids.
// Activities with a NULL task_id (drafts with no type chosen yet) are skipped by the inner join — their answers stay
// and become reachable again if the draft goes on to select the "Other ..." type.
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721140000_BLUM_7092_ScopePeopleAndIssuesToOtherType")]
public partial class BLUM_7092_ScopePeopleAndIssuesToOtherType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;

            DELETE FROM activity_answers aa
            USING activities a, activity_tasks t
            WHERE aa.activity_id = a.id
              AND a.task_id = t.id
              AND aa.question_code IN (
                  'EDU_WHO_FOR', 'EDU_WHO_FOR_OTHER', 'EDU_RELATED_ISSUES', 'EDU_RELATED_ISSUES_OTHER',
                  'HELP_WHO_FOR', 'HELP_WHO_FOR_OTHER', 'HELP_RELATED_ISSUES', 'HELP_RELATED_ISSUES_OTHER')
              AND t.task_name NOT IN (
                  'Other type of education, campaign or outreach',
                  'Other type of help, advice or service');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and the seeder rebuilds the schema on the next
        // boot regardless of which direction we arrived from.
    }
}