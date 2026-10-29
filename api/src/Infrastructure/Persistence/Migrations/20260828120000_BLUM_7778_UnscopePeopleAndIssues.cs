using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7778: the "People and issues" screen (protocol 1.EF.1.5/1.6) must be shown for every activity type in
// Education, campaigns and outreach and in Help, advice and access to services — not only for each category's
// "Other ..." type. That scoping came from BLUM-7092; DHSC has reversed the decision, so SeedFormDesigner now
// creates the section category-scoped only, with no activity_task_id, and FormSection.AppliesToTask answers
// true whatever type the LA picked.
//
// Seed content lives only in SeedFormDesigner (migrations never INSERT or UPDATE it), and that seeder is
// deliberately seed-if-empty so DHSC admins own the schema through the Form Designer after its first run. So the
// change ships as this one-shot cleanup: clear the form schema tables, and the seeder rebuilds the canonical set —
// with the section unscoped — on the next boot. Runs exactly once per database (__EFMigrationsHistory), never on
// every boot.
//
// Deliberately narrow. Activities, activity_expected_results and submissions are all kept: answers resolve by their
// snapshotted question_code (ActivityAnswer.QuestionId is write-only — no read path joins it), so re-minted question
// ids strand nothing. Categories, activity_tasks and key_result_* are untouched, and submission_activity_answers
// holds quarterly-report codes rather than activity-schema ones, so it needs nothing either. The one cost is any
// section or question a DHSC admin authored through the Form Designer before this deploy — the guard needs
// form_sections empty, and the seeder only rebuilds the canonical set (none expected at MVP; called out in the PR).
//
// No activity_answers statement, unlike BLUM-7092's mirror image. That migration NARROWED the section's scope and
// had to drop the EDU_/HELP_ rows it made unreachable; this one WIDENS it, so no existing answer stops being
// readable. The opposite is true instead: Education/Help activities completed since 21 July on a non-"Other" type
// have no EDU_/HELP_WHO_FOR row at all, and the screen becomes required for them the moment they are next edited.
// That is left to degrade in the UI rather than patched here — the view page renders every visible question of
// every applicable section ("Not provided"), and the editor validates all sections client-side before saving, so
// the LA is taken to the newly visible screen with a real inline error instead of an opaque 422.
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260828120000_BLUM_7778_UnscopePeopleAndIssues")]
public partial class BLUM_7778_UnscopePeopleAndIssues : Migration
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
        // Nothing to undo — the deleted rows are seed content, and only the seeder writes them.
        //
        // Rolling back does NOT put the "Other ..." scoping back. By the time anything could run Down, Up's boot
        // has already repopulated the schema, so the seed-if-empty guard is armed again and redeploying the
        // previous app version rebuilds nothing. Getting the scoping back means clearing form_sections again —
        // the same one-shot cleanup pointed the other way.
    }
}