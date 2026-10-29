using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7790: DHSC has withdrawn the "Meeting attendance" key result ([B] on the measures screen for the
// "Setting up and running a local steering group" type, Strategy and leadership) and the "Who attended each
// meeting?" question it unlocked. Both arrived in BLUM-7620 from the 13 August Question Protocol; the meeting
// count measure they were built around has since been dropped, and the question was self-contradictory anyway
// (its label asks who attended while both of its error messages are percentage errors, which is why it was
// seeded as a bounded percentage and flagged back to DHSC). KeyResultProtocolSeedData no longer carries either.
//
// Seed content lives only in KeyResultProtocolSeedData (migrations never INSERT or UPDATE it), and
// SeedKeyResultProtocolAsync is guarded by a single `if (KeyResultQuestions.AnyAsync()) return;` — so the
// removal cannot reach an already-seeded database without clearing that guard. This is that one-shot clear; the
// seeder rebuilds the whole protocol on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Both key-result tables must go: that one guard covers a pass that writes key_result_definitions AND
// key_result_questions, so leaving the definitions behind would re-insert them and violate their unique
// (activity_task_id, code) index — and a seed failure is fatal to startup, i.e. a crash-looping container.
//
// The third statement is the one that is NOT optional, unlike the answer scrubs in the reseeds this migration is
// modelled on. activity_expected_results.key_result_code has no FK, so a saved 'lsg_attendance' selection
// survives the reseed and strands its activity: splitExpectedResults hydrates every saved code back into the
// wizard state and buildExpectedResultsPayload sends them all back on the next save, while the checkbox itself no
// longer renders because the taxonomy no longer offers it — so the LA cannot untick it, and
// ExpectedResultInputs.Normalise refuses the unknown code with a 422 the screen gives them no way to clear.
// Two accepted costs. An activity whose ONLY measure was "Meeting attendance" drops to zero and fails the minimum
// of 1 the next time it is completed — that degrades as a real inline error on the measures step, not an opaque
// 422. And the delete is unconditional, so it also reaches activities nobody can edit any more: a submission
// finalised before snapshots existed has a null ReportSnapshotJson and is re-rendered live from current data by
// QuarterlyReportExportService.GetOrBuildSnapshotAsync, so such a report loses the measure from its list as well
// as the answer below. Stored snapshots keep both. Narrowing the delete to still-editable activities is not an
// option — that is exactly the set the 422 strands — so the rewritten history is taken deliberately, on MVP-tier
// data, and called out in the PR.
//
// The fourth drops the report answers the removal makes unreachable. Live reads already skip them —
// KeyResultQuestionResolver walks the question bank, and BuildCarryForward filters codes that no longer resolve —
// but a submission finalised before snapshots existed re-renders live from the current protocol, and nothing
// would ever clear these rows again. Keyed on question_code because this migration re-mints every question id.
//
// Untouched: activities, activity_answers, submissions and the form schema tables. The activity-record schema is
// seeded separately by SeedFormDesigner and names nothing here, so no DHSC-authored Form Designer content is at
// risk — key_result_* is system-owned seed with no admin write path.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260828170000_BLUM_7790_RemoveMeetingAttendance")]
public partial class BLUM_7790_RemoveMeetingAttendance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM key_result_questions;
            DELETE FROM key_result_definitions;

            DELETE FROM activity_expected_results WHERE key_result_code = 'lsg_attendance';
            DELETE FROM submission_activity_answers WHERE question_code = 'LSG_ATTENDANCE';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and only the seeder writes them.
        //
        // Rolling back does NOT restore the attendance result or its question, unlike the earlier reseeds this
        // migration is modelled on. Those rebuilt the same content whichever version booted next; this one changes
        // what the canonical set contains. By the time anything could run Down, Up's boot has already repopulated
        // the protocol, so the seed-if-empty guard is armed again and redeploying the previous app version rebuilds
        // nothing. Getting them back means clearing key_result_questions and key_result_definitions again — the same
        // one-shot cleanup pointed the other way. The deleted selections and answers are unrecoverable either way.
    }
}