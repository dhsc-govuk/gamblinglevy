using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7645: "Which services are currently screening for gambling harm?" (SCR_SERVICES, the Closed/Finished variant)
// now prefills from the activity record's "Who have you worked with, or who will you work with?" answer, and its
// conditional companion SCR_SERVICES_OTHER from the matching _WHO_WITH_OTHER free text. BLUM-7475 had made the
// question a checkbox list over the shared WhoWith options but deliberately left it prefill-free; DHSC overturned
// that call. The change is one seeded column each (key_result_questions.prefill_source) — PrefillSource already
// exists on the entity and the table, so there is no schema change here.
//
// Seed content lives only in KeyResultProtocolSeedData (migrations never INSERT or UPDATE it), and
// SeedKeyResultProtocolAsync is guarded by a single `if (KeyResultQuestions.AnyAsync()) return;` — so the changed
// rows cannot reach an already-seeded database without clearing the guard. This is that one-shot clear; the seeder
// rebuilds the whole protocol on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Both tables must go: that one guard covers a pass that writes key_result_definitions AND key_result_questions, so
// leaving the definitions behind would re-insert them and violate their unique (activity_task_id, code) index — a seed
// failure is fatal to startup.
//
// Unlike BLUM-7475 no answer data is touched. No question code, field type or option list changes, so every saved
// submission_activity_answers row still validates against the question it was written for. Re-minting key-result ids
// strands nothing either: every read path resolves by stable string code.
//
// Prefill is a read-time overlay (KeyResultQuestionResolver.BuildPrefill, applied in
// QuarterlyReportControllerService.GetActivityQuestionsAsync), so reports already in flight pick it up on the next
// GET wherever the LA has not yet saved an answer of its own — a saved answer still wins. Completed activity reports
// keep their Status and their saved answers untouched.
//
// One accepted cost on an environment that already holds reports — the mirror of the one BLUM-7475 took. A submission
// finalised before snapshots existed has a null ReportSnapshotJson, and QuarterlyReportExportService
// .GetOrBuildSnapshotAsync re-renders those live from the CURRENT protocol, so such an export can now show
// SCR_SERVICES populated from the activity's "Who will you work with?" answer even though the LA never answered it.
// Stored snapshots are unaffected. Accepted for MVP-tier data, as BLUM-7475 accepted the same shape in reverse.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824150000_BLUM_7645_ReseedKeyResultProtocol")]
public partial class BLUM_7645_ReseedKeyResultProtocol : Migration
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
        // Nothing to undo — the deleted rows are seed content, and the seeder rebuilds the protocol on the next
        // boot regardless of which direction we arrived from.
    }
}