using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7788: DHSC's protocol draws "What is their level of engagement?" as a multi-row radio — one row per member
// picked on "Who is in the steering group?". BLUM-7620 transcribed it as a single LSG_ENGAGEMENT radio because that
// control does not exist, and flagged the substitution back to DHSC; this is DHSC asking for the real thing. The
// question is now seeded once per member option of LSG_MEMBERS (LSG_ENGAGEMENT_LICENSING, LSG_ENGAGEMENT_POLICE, …),
// each revealed by that member being ticked, so the bare LSG_ENGAGEMENT code no longer exists.
//
// Seed content lives only in KeyResultProtocolSeedData (migrations never INSERT or UPDATE it), and
// SeedKeyResultProtocolAsync is guarded by a single `if (KeyResultQuestions.AnyAsync()) return;` — so the change
// cannot reach an already-seeded database without clearing that guard. This is that one-shot clear; the seeder
// rebuilds the whole protocol on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Both key-result tables must go: that one guard covers a pass that writes key_result_definitions AND
// key_result_questions, so leaving the definitions behind would re-insert them and violate their unique
// (activity_task_id, code) index — and a seed failure is fatal to startup, i.e. a crash-looping container.
//
// The third statement drops the answers saved under the retired code. Nothing resolves LSG_ENGAGEMENT any more, so
// they are already invisible everywhere: KeyResultQuestionResolver walks the question bank, BuildCarryForward
// filters codes that no longer resolve, and ReportSnapshotService only emits resolved questions — including for a
// submission finalised before snapshots existed, which QuarterlyReportExportService.GetOrBuildSnapshotAsync
// re-renders live from the current protocol. Left behind they would simply never be read or cleared again. They are
// not migrated onto a per-member code either: one saved level cannot be attributed to any one of the members it was
// entered for, and guessing would put an answer the LA never gave against a named organisation. Such a report loses
// the engagement row and, if it is still editable, asks for a level per member the next time it is opened.
//
// Unlike BLUM-7790 there is no activity_expected_results scrub: the lsg_members key result is untouched, so no
// saved measure is stranded by this reseed.
//
// The fourth statement is what stops this reaching DHSC as silently missing data, and it is needed here precisely
// because this migration ADDS required questions where BLUM-7790 only removed one. A report is submittable purely
// on stored status: SubmitReportCommand gates on Submission.AreAllSectionsCompleted and never re-runs
// SubmissionAnswerValidator. So an LA that had already walked its steering-group activity through
// Check-your-answers in a still-open window would keep `completed`, lose its one engagement answer to the third
// statement, gain up to 23 required questions nothing would ever ask it, and submit a report whose per-member rows
// are all blank — strictly less than it recorded before the change. Rolling those reports (and the "Enter activity
// results" section they roll up into) back to in_progress puts the LA back through the page, where the new
// questions are enforced the ordinary way. Narrowed to what is actually affected: unsubmitted submissions,
// activities that selected the lsg_members measure, and Active ones — every steering-group question is
// active-scope, so a Closed or Finished activity resolves none of them and needs no revisit. This is user
// progress, not seed content, so resetting it does not breach the "migrations never write seed content" rule.
//
// Untouched: activities, activity_answers, submissions and the form schema tables. The activity-record schema is
// seeded separately by SeedFormDesigner and names nothing here, so no DHSC-authored Form Designer content is at
// risk — key_result_* is system-owned seed with no admin write path.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260828180000_BLUM_7788_PerMemberSteeringGroupEngagement")]
public partial class BLUM_7788_PerMemberSteeringGroupEngagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM key_result_questions;
            DELETE FROM key_result_definitions;

            DELETE FROM submission_activity_answers WHERE question_code = 'LSG_ENGAGEMENT';

            WITH rolled_back AS (
                UPDATE submission_activity_reports r
                SET status = 'in_progress', updated_at = now()
                FROM submissions s, activities a
                WHERE r.submission_id = s.id
                  AND r.activity_id = a.id
                  AND s.submitted_at IS NULL
                  AND r.status = 'completed'
                  AND a.status = 'active'
                  AND EXISTS (
                      SELECT 1
                      FROM activity_expected_results e
                      WHERE e.activity_id = a.id AND e.key_result_code = 'lsg_members')
                RETURNING r.submission_id)
            UPDATE submission_sections
            SET status = 'in_progress', updated_at = now()
            WHERE module_code = 'activity_key_results'
              AND status = 'completed'
              AND submission_id IN (SELECT submission_id FROM rolled_back);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and only the seeder writes them.
        //
        // Rolling back does NOT restore the single engagement question. By the time anything could run Down, Up's
        // boot has already repopulated the protocol, so the seed-if-empty guard is armed again and redeploying the
        // previous app version rebuilds nothing. Getting it back means clearing key_result_questions and
        // key_result_definitions again — the same one-shot cleanup pointed the other way. The deleted answers are
        // unrecoverable either way, and the reports rolled back to in_progress stay there: re-completing them is
        // one click on Check-your-answers, and re-marking them completed here would hand back the very "submittable
        // with no engagement data" state Up exists to prevent.
    }
}