using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7475: "Which services are currently screening for gambling harm?" rendered as a free-text box. The protocol
// sheet marks it "List/text" but supplies no options, so BLUM-6933 transcribed it as text — and ProtocolFieldTypes.Text
// maps to a Textarea. KeyResultProtocolSeedData now seeds it as a list over the shared "Who will you work with?"
// options (as the sheet does for TRN_SERVICE_AREAS), plus the conditional "Other" free-text companion that every list
// offering "other" needs, under a new SCR_SERVICES code — the Active variant of SCR_TOOL is a yes/no, and one code
// cannot answer to two validators.
//
// Seed content lives only in KeyResultProtocolSeedData (migrations never INSERT or UPDATE it), and
// SeedKeyResultProtocolAsync is guarded by a single `if (KeyResultQuestions.AnyAsync()) return;` — so the changed rows
// cannot reach an already-seeded database without clearing the guard. This is that one-shot clear; the seeder rebuilds
// the whole protocol on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Both tables must go: that one guard covers a pass that writes key_result_definitions AND key_result_questions, so
// leaving the definitions behind would re-insert them and violate their unique (activity_task_id, code) index — a seed
// failure is fatal to startup.
//
// The third statement drops answers saved under the retired Closed/Finished SCR_TOOL question. They are prose, and
// SCR_TOOL now only ever resolves as the Active yes/no radio, so an activity flipped back to Active would have its old
// sentence validated against the yes/no allow-list — a 422 blocking Complete with nothing on screen to correct. Real
// yes/no answers are deliberately spared. Nothing else is touched: activities, activity_answers,
// activity_expected_results, submissions and the Form Designer tables (form_sections/form_questions) all survive, and
// re-minted key-result ids strand nothing because every read path resolves by stable string code.
//
// Two costs on an environment that already holds reports, both accepted for MVP-tier data (BLUM-7475 was raised before
// go-live):
//
// 1. A report whose screening activity was already completed keeps Status = Completed, and so does its section
//    roll-up — nothing recomputes either on read, and SubmitReportCommand gates on AreAllSectionsCompleted rather than
//    re-running the validator. So that LA can submit with the now-required SCR_SERVICES unanswered. Note this follows
//    from retiring the old question at all, not from the DELETE: the answer is orphaned either way, because SCR_TOOL no
//    longer resolves for a Closed activity. Any change that adds or re-codes a required protocol question has the same
//    shape (BLUM-7472 shipped it one commit earlier).
// 2. A submitted report keeps its frozen snapshot only if one was stored. Submissions finalised before snapshots
//    existed have a null ReportSnapshotJson, and QuarterlyReportExportService.GetOrBuildSnapshotAsync re-renders those
//    live from the CURRENT protocol and answers — so their export loses this question and its answer.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260819140000_BLUM_7475_ReseedKeyResultProtocol")]
public partial class BLUM_7475_ReseedKeyResultProtocol : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM key_result_questions;
            DELETE FROM key_result_definitions;
            DELETE FROM submission_activity_answers WHERE question_code = 'SCR_TOOL' AND value NOT IN ('yes', 'no');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and the seeder rebuilds the protocol on the next
        // boot regardless of which direction we arrived from.
    }
}