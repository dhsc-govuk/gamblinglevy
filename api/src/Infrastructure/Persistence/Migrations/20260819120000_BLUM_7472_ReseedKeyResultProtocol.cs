using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7472: four quarterly-report questions offered an "Other" checkbox with nowhere to say what "Other" was
// ({RNA,LNA}_INFORMATION, LSG_MEMBERS, TRN_SERVICE_AREAS). SubmissionAnswerValidator enforces the option
// allow-list on draft saves too, so free text in the parent code is a 422 and the export prints the bare label
// "Other" — there was no workaround. KeyResultProtocolSeedData now carries the missing conditional *_OTHER text
// companions, modelled on the {RNA,LNA}_CONTRIBUTORS_OTHER rows that already shipped.
//
// Seed content lives only in KeyResultProtocolSeedData (migrations never INSERT or UPDATE it), and
// SeedKeyResultProtocolAsync is guarded by a single `if (KeyResultQuestions.AnyAsync()) return;` — so the new rows
// cannot reach an already-seeded database without clearing the guard. This is that one-shot clear; the seeder
// rebuilds the whole protocol on the next boot. Runs exactly once per database (__EFMigrationsHistory).
//
// Both tables must go: that one guard covers a pass that writes key_result_definitions AND key_result_questions,
// so leaving the definitions behind would re-insert them and violate their unique (activity_task_id, code) index —
// a seed failure is fatal to startup.
//
// Deliberately narrow, and non-destructive to user data. Nothing holds a foreign key to either table (both are
// referenced by id-only Guid columns), and the read paths resolve by stable string code: SubmissionActivityAnswer
// snapshots question_code, ActivityExpectedResult stores key_result_code. So re-minted ids strand nothing —
// activities, activity_answers, activity_expected_results, submissions and every answer row are kept, as are the
// Form Designer tables (form_sections/form_questions), which earlier reseeds had to clear and this one does not.
// The one behavioural cost: an activity report completed with "Other" ticked now has an unanswered required
// companion, so re-completing it asks for the free text. Accepted for MVP-tier data.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260819120000_BLUM_7472_ReseedKeyResultProtocol")]
public partial class BLUM_7472_ReseedKeyResultProtocol : Migration
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