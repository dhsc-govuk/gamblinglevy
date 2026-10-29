using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7791: the "Documenting a strategy and action plan" screen is re-transcribed from a LATER REVISION of the
// 13 August Question Protocol ("Post-Assessment Question Protocol 13 August .xlsx" — note the corrected spelling;
// BLUM-7620 worked from "…Protocal 13 August.xlsx"). Four strings change, all on Results-table rows 18-21:
//
//   SAP_HWB_PRESENTED  active           error  → "Select yes if you are planning on presenting to the Health and Wellbeing Board"
//   SAP_HWB_PRESENTED  closed_finished  error  → "Select yes if you have presented to the Health and Wellbeing Board"
//   SAP_PLAN_DEVELOPED closed_finished  error  → "Select yes if a draft has been developed"
//   SAP_PLAN_DEVELOPED active           label  → "Is a draft strategy and/or action plan being developed?"
//
// The ticket's conditional-display rules — the Board questions shown and required only when the
// sap_hwb_presented measure was selected, Active asking "Will you present…" and Closed/Finished "Have you
// presented…" — already hold: BLUM-7620 split that key result out and seeded the two phrasings as one Code
// under two StatusScopes, which KeyResultQuestionResolver filters on. Nothing about that mechanism moves here;
// this migration exists purely so the reworded copy can reach an already-seeded database, and the rules are
// now pinned by tests (QuestionProtocolAugustTests, KeyResultProtocolSeedDataTests) so they cannot regress
// unnoticed the way they were previously only true by construction.
//
// Seed content lives only in KeyResultProtocolSeedData (migrations never INSERT or UPDATE it), and
// SeedKeyResultProtocolAsync is guarded by a single `if (KeyResultQuestions.AnyAsync()) return;` — so a copy
// change cannot reach a seeded database without clearing that guard. This is that one-shot clear; the seeder
// rebuilds the whole protocol on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Both key-result tables must go: that one guard covers a pass writing key_result_definitions AND
// key_result_questions, so leaving the definitions behind would re-insert them and violate their unique
// (activity_task_id, code) index — a fatal seed error, i.e. a crash-looping container.
//
// Nothing else is needed, and it is worth saying why, because the three preceding protocol migrations each
// needed more:
//   • No submission_activity_answers scrub (contrast BLUM-7788, which retired LSG_ENGAGEMENT). Every question
//     Code survives untouched, so no saved answer is orphaned and BuildCarryForward still resolves them all.
//   • No activity_expected_results scrub (contrast BLUM-7790, which withdrew a key result). No key result is
//     removed and no key-result LABEL changes, so no key_result_code is stranded and no `text` snapshot of a
//     selected measure goes stale.
//   • No submission_activity_reports / submission_sections rollback to in_progress (contrast BLUM-7788, which
//     added required questions). The same questions are asked of the same activities as before, so no report
//     already marked completed can submit a blank it was never given the chance to fill.
//
// One consequence to expect rather than fix: a submission whose report snapshot was already frozen keeps the
// previous label, and QuarterlyReportExportService re-renders live only where no snapshot exists. That is the
// right way round — a copy change should not rewrite what an LA actually submitted.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260831120000_BLUM_7791_StrategyScreenProtocolCopy")]
public partial class BLUM_7791_StrategyScreenProtocolCopy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM key_result_questions;
            DELETE FROM key_result_definitions;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and only the seeder writes them.
        //
        // Rolling back does NOT restore the previous wording. By the time anything could run Down, Up's boot has
        // already repopulated the protocol, so the seed-if-empty guard is armed again and redeploying the earlier
        // app version rebuilds nothing. Getting the old copy back means clearing key_result_questions and
        // key_result_definitions again — the same one-shot cleanup pointed the other way. No answer, activity or
        // submission is touched either way.
    }
}