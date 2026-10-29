using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7871: DHSC has withdrawn "Is the role shared with other local authorities?" (PR_ROLE_SHARED) from the
// People and roles "Role details" screen. BLUM-7860 made Joint partnership its own activity category, so a role
// funded across authorities is now recorded as a partnership record rather than as a flag on a People and roles
// one, and the question is redundant. SeedFormDesigner no longer seeds it; Role details keeps PR_FUNDING_TIME
// (order 0) and PR_ROLE_DURATION (order 1), which stay contiguous, so nothing needs renumbering.
//
// Not to be confused with {ST,LIC,EDU,HELP}_SHARED, "Was this activity shared with other local authorities, or
// will it be shared?" (protocol 1.BCDEF.1.4). That is a different question on a different screen and stays;
// BLUM-7759 removed only its Needs assessment copy. This migration touches neither.
//
// Seed content lives only in SeedFormDesigner (migrations never INSERT or UPDATE it, so DHSC admins keep
// ownership of the schema through the Form Designer after its first run), and that seeder is seed-if-empty on
// form_sections — it has already run on every deployed environment and would otherwise never drop the question.
// So the change ships as the established one-shot cleanup: clear the three form-schema tables so the guard
// re-arms, and the seeder rebuilds the whole canonical set — without PR_ROLE_SHARED, and still including the
// Joint partnership section BLUM-7860 added hours earlier — on the next boot. Runs exactly once per database
// (__EFMigrationsHistory), never on every boot.
//
// Deliberately narrow, and modelled on BLUM-7759. Activities, activity_expected_results, submissions,
// categories, activity_tasks and key_result_* are all kept: answers resolve by their snapshotted question_code
// (ActivityAnswer.QuestionId is write-only — no read path joins it), so re-minted question ids strand nothing,
// and submission_activity_answers holds quarterly-report codes rather than activity-schema ones. The one cost is
// any section or question a DHSC admin authored through the Form Designer before this deploy — the guard needs
// form_sections empty, and the seeder only rebuilds the canonical set (none expected at MVP; called out in the
// PR).
//
// The second statement drops the activity_answers rows the removal makes unreachable. Every surface — wizard,
// editor, check-answers, view — renders the schema and skips a code that is no longer in it;
// ActivityAnswerValidator.CollectPersistable walks the schema's questions rather than the request's, so
// UpdateActivityCommand would never replace or clear these rows again; and nothing reads the value downstream
// (report prefill matches only _WHO_WITH/_WHO_WITH_OTHER, and no export names activity answers at all). Keyed on
// question_code because this migration re-mints question ids, and scoped to the exact code so the four surviving
// {ST,LIC,EDU,HELP}_SHARED answers are untouched.
//
// DEPLOY NOTE, inherited from BLUM-7759/7860 and worth stating now that this is the third repetition: the
// tables are emptied by MigrateAsync and only refilled by SeedDataAsync a moment later, so there is a window
// in which form_sections is empty. During it activities/form-schema returns no questions, and because
// ActivityAnswerValidator.Validate iterates the SCHEMA's questions rather than the request's, a completing
// POST /activities passes with zero detail answers and nothing is logged. Deploy as a single-instance restart
// rather than a rolling one, or accept that any activity completed in those seconds keeps no detail answers.
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260907140000_BLUM_7871_DropRoleSharedQuestion")]
public partial class BLUM_7871_DropRoleSharedQuestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;

            DELETE FROM activity_answers WHERE question_code = 'PR_ROLE_SHARED';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and only the seeder writes them.
        //
        // Rolling back does NOT restore PR_ROLE_SHARED, the same way BLUM-7759 could not restore NA_SHARED. By the
        // time anything could run Down, Up's boot has already repopulated the schema, so the seed-if-empty guard
        // is armed again and redeploying the previous app version rebuilds nothing. Getting the question back
        // means clearing form_sections again — the same one-shot cleanup pointed the other way. The deleted
        // activity_answers rows are unrecoverable either way.
    }
}