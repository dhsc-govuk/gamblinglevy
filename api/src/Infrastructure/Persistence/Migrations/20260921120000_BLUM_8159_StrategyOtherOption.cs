using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-8159: "Which other strategies have been updated to include gambling harms?" (SAP_OTHER_STRATEGIES,
// Closed/Finished) offered Alcohol, Drugs, Mental health and Suicide, but not the "Other" the data map lists last.
// KeyResultProtocolSeedData.OtherStrategies now ends with it, and — because a ticked "Other" with nowhere to say what
// it was is the BLUM-7472 gap — gains a conditional free-text companion, SAP_OTHER_STRATEGIES_OTHER.
//
// Seed content lives only in KeyResultProtocolSeedData (migrations never INSERT or UPDATE it), and
// SeedKeyResultProtocolAsync is guarded by a single `if (KeyResultQuestions.AnyAsync()) return;` — so the new option
// and row cannot reach an already-seeded database without clearing that guard. This is that one-shot clear; the
// seeder rebuilds the whole protocol on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Both key-result tables must go: that one guard covers a pass writing key_result_definitions AND
// key_result_questions, so leaving the definitions behind would re-insert them and violate their unique
// (activity_task_id, code) index — a fatal seed error, i.e. a crash-looping container.
//
// Nothing else is needed:
//   • No submission_activity_answers scrub. The change only ADDS an option value and a question, so every saved
//     answer still validates against its question's option list.
//   • No activity_expected_results scrub. No key result is removed and no key-result label changes.
//   • No submission_activity_reports / submission_sections rollback to in_progress. The new companion is required,
//     but only while "other" is ticked — and until now SubmissionAnswerValidator refused "other", so no saved answer
//     contains it and no already-completed report can reveal a blank it was never asked.
//   • The Form Designer tables (form_sections/form_questions) are untouched.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921120000_BLUM_8159_StrategyOtherOption")]
public partial class BLUM_8159_StrategyOtherOption : Migration
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
        // Rolling back does NOT remove the option. By the time anything could run Down, Up's boot has already
        // repopulated the protocol, so the seed-if-empty guard is armed again and redeploying the earlier app version
        // rebuilds nothing. No answer, activity or submission is touched either way.
    }
}