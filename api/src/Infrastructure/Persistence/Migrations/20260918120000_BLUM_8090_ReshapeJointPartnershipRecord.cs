using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-8090: the Joint partnership record is reshaped to the Figma. Its "Activity details" section drops its
// description and three of its five questions — JP_PROGRESS, JP_CHALLENGES and JP_NEXT_QUARTER move into the
// quarterly report, where a partnership now answers the module's AKR_DELIVERY_* questions in its own wording — and
// JP_PARTNERS becomes a single-line Text question with a hint. JP_COMMITTED gets a new hint.
//
// Seed content lives only in SeedFormDesigner (migrations never INSERT or UPDATE it, so DHSC admins keep ownership of
// the schema through the Form Designer after its first run), and that seeder is seed-if-empty on form_sections — it
// has already run on every deployed environment. So the schema change ships as the established one-shot cleanup
// (BLUM-7759/7860/7871): clear the three form-schema tables so the guard re-arms, and the seeder rebuilds the whole
// canonical set on the next boot. Runs exactly once per database (__EFMigrationsHistory), never on every boot. The
// one cost is any section or question a DHSC admin authored through the Form Designer before this deploy (none
// expected at MVP; called out in the PR). Children are deleted before parents (option -> question -> section FK).
//
// The second statement drops the activity_answers rows the removal makes unreachable, exactly as BLUM-7871 did for
// PR_ROLE_SHARED: every surface renders the schema and skips a code no longer in it, ActivityAnswerValidator
// .CollectPersistable walks the schema's questions so no later save would replace or clear them, and nothing reads
// them downstream (report prefill matches only _WHO_WITH/_WHO_WITH_OTHER). They are not copied into the quarterly
// report: a record's progress answer was never tied to a reporting period, and the product decision was to drop
// them. Keyed on question_code because this migration re-mints question ids. The name and start date a partnership
// record may already hold are deliberately left alone — no longer collected or shown, but a record that has a name
// keeps being listed by it.
//
// The third statement closes a submit-gate hole the reversal opens. Until now a partnership was excluded from "Enter
// activity results", so ConfirmActivitiesCommand could roll that section up to Completed for an LA whose only
// reportable activities were partnerships (or whose other activities were all reported). Partnerships now have to be
// reported there, but a stored Completed status is only re-derived on the next confirm, save or complete — so an
// unsubmitted report in that state could still be submitted with its partnership never reported. Put such a section
// back where RecomputeSectionStatusAsync would: In progress when the report has any activity report rows, Not
// started otherwise. Scoped to unsubmitted reports whose organisation has a reportable partnership (complete, not
// deleted, not Planned — ActivityReportContextResolver.IsReportable) with no Completed activity report.
//
// DEPLOY NOTE, as for BLUM-7759/7860/7871: the tables are emptied by MigrateAsync and only refilled by SeedDataAsync
// a moment later, so there is a window in which form_sections is empty. During it activities/form-schema returns no
// questions, and because ActivityAnswerValidator.Validate iterates the SCHEMA's questions rather than the request's,
// a completing POST /activities passes with zero detail answers and nothing is logged. Deploy as a single-instance
// restart rather than a rolling one, or accept that any activity completed in those seconds keeps no detail answers.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260918120000_BLUM_8090_ReshapeJointPartnershipRecord")]
public partial class BLUM_8090_ReshapeJointPartnershipRecord : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;

            DELETE FROM activity_answers WHERE question_code IN ('JP_PROGRESS', 'JP_CHALLENGES', 'JP_NEXT_QUARTER');

            UPDATE submission_sections AS sec
            SET status = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM submission_activity_reports AS report
                        WHERE report.submission_id = sec.submission_id)
                        THEN 'in_progress'
                    ELSE 'not_started'
                END
            FROM submissions AS submission
            WHERE sec.submission_id = submission.id
              AND sec.module_code = 'activity_key_results'
              AND sec.status = 'completed'
              AND submission.submitted_at IS NULL
              AND EXISTS (
                  SELECT 1
                  FROM activities AS activity
                  JOIN categories AS category ON category.id = activity.category_id
                  WHERE activity.organisation_id = submission.organisation_id
                    AND category.name = 'Joint partnership'
                    AND activity.is_complete
                    AND activity.deleted_at IS NULL
                    AND activity.status IN ('active', 'closed', 'finished')
                    AND NOT EXISTS (
                        SELECT 1 FROM submission_activity_reports AS report
                        WHERE report.submission_id = submission.id
                          AND report.activity_id = activity.id
                          AND report.status = 'completed'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted form rows are seed content, and only the seeder writes them.
        //
        // Rolling back does NOT restore the previous Joint partnership section, the same way BLUM-7871 could not
        // restore PR_ROLE_SHARED: by the time anything could run Down, Up's boot has already repopulated the schema,
        // so the seed-if-empty guard is armed again and redeploying the previous app version rebuilds nothing. The
        // deleted activity_answers rows are unrecoverable either way, and a section status put back to In progress
        // simply re-derives on the LA's next save.
    }
}