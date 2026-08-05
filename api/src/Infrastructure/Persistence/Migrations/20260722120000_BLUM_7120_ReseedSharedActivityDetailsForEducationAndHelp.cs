using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7120: Education and Help never received the shared Activity details questions (protocol 1.BCDEF.1.1,
// 1.BCDEF.1.2 and 1.BCDEF.1.4) — SeedFormDesigner only seeded that section for the three generic categories.
// The seeder now seeds it for all five non-People categories (EDU_/HELP_ WHO_WITH, WHO_WITH_OTHER and SHARED),
// with People and issues moved after it. Seed content lives only in SeedFormDesigner (migrations never INSERT
// or UPDATE it), and that seeder is deliberately seed-if-empty so DHSC admins own the schema through the Form
// Designer after its first run. So the fix ships as this one-shot cleanup: clear the form schema tables, and
// the seeder rebuilds the canonical set — Activity details included for Education and Help — on the next boot.
// Runs exactly once per database (__EFMigrationsHistory), never on every boot.
//
// Deliberately narrow. Activities, activity_answers, activity_expected_results and submissions are all kept:
// answers resolve by their snapshotted question_code (ActivityAnswer.QuestionId is write-only — no read path joins
// it), so re-minted question ids strand nothing. This fix only ADDS questions, so no answers become unreachable —
// but pre-existing completed Education/Help activities lack the new required answers, and they degrade like this:
// the view page omits the unanswered questions entirely (only the optional name row renders "Not provided"); any
// edit of such a record — even a status-only change — is rejected until the new questions are answered, because
// UpdateActivityCommand enforces required answers across the whole applicable schema for completed activities; and
// read-only closed/finished records keep the gap permanently. Accepted for MVP-tier data (only QA/test records
// exist on deployed environments). Categories, activity_tasks and key_result_* are untouched. The one cost is any
// section or question a DHSC admin authored through the Form Designer before this deploy — the guard needs
// form_sections empty, and the seeder only rebuilds the canonical set (none expected at MVP; called out in the PR).
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260722120000_BLUM_7120_ReseedSharedActivityDetailsForEducationAndHelp")]
public partial class BLUM_7120_ReseedSharedActivityDetailsForEducationAndHelp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM form_question_options;
            DELETE FROM form_questions;
            DELETE FROM form_sections;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Nothing to undo — the deleted rows are seed content, and the seeder rebuilds the schema on the next
        // boot regardless of which direction we arrived from.
    }
}