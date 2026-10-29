using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7759: the Activity details design hides the shared-authority question (protocol 1.BCDEF.1.4) for Needs
// assessment — "Not displayed if 'Activity category' == 'Needs assessment'" — so SeedActivityDetailsSection now
// takes an includeShared flag and NA_SHARED is no longer seeded. Strategy, Licensing, Education and Help keep
// theirs, and PR_ROLE_SHARED on People and roles is a different question entirely.
//
// Seed content lives only in SeedFormDesigner (migrations never INSERT or UPDATE it), and that seeder is
// deliberately seed-if-empty so DHSC admins own the schema through the Form Designer after its first run. So the
// change ships as this one-shot cleanup: clear the form schema tables, and the seeder rebuilds the canonical set —
// without NA_SHARED — on the next boot. Runs exactly once per database (__EFMigrationsHistory), never on every
// boot. Note this is the third time Needs assessment has changed hands on this question (deactivated 17 July,
// restored by BLUM-7081, removed here); the seeder comment explains which way is current.
//
// Deliberately narrow. Activities, activity_expected_results and submissions are all kept: answers resolve by their
// snapshotted question_code (ActivityAnswer.QuestionId is write-only — no read path joins it), so re-minted question
// ids strand nothing. Categories, activity_tasks and key_result_* are untouched, and submission_activity_answers
// holds quarterly-report codes rather than activity-schema ones, so it needs nothing either. The one cost is any
// section or question a DHSC admin authored through the Form Designer before this deploy — the guard needs
// form_sections empty, and the seeder only rebuilds the canonical set (none expected at MVP; called out in the PR).
//
// The second statement drops the activity_answers rows the removal makes unreachable. Every surface — wizard,
// editor, check-answers, view — renders the schema and skips a code that is no longer in it, and
// UpdateActivityCommand only replaces answers to questions the schema still returns, so these rows would never be
// read or cleared again. Keyed on question_code because this migration re-mints question ids, and scoped to the
// exact code so ST_/LIC_/EDU_/HELP_SHARED and PR_ROLE_SHARED are untouched.
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260827120000_BLUM_7759_DropSharedQuestionForNeedsAssessment")]
public partial class BLUM_7759_DropSharedQuestionForNeedsAssessment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;

            DELETE FROM activity_answers WHERE question_code = 'NA_SHARED';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and only the seeder writes them.
        //
        // Rolling back does NOT restore NA_SHARED, unlike the earlier reseeds this migration is modelled on. Those
        // rebuilt the same content whichever version booted next; this one changes what the canonical set contains.
        // By the time anything could run Down, Up's boot has already repopulated the schema, so the seed-if-empty
        // guard is armed again and redeploying the previous app version rebuilds nothing. Getting the question back
        // means clearing form_sections again — the same one-shot cleanup pointed the other way. The deleted
        // activity_answers rows are unrecoverable either way.
    }
}