using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7860: the new Joint partnership category needs its own "Activity details" section (JP_COMMITTED,
// JP_PARTNERS, JP_PROGRESS, JP_CHALLENGES, JP_NEXT_QUARTER), and SeedFormDesigner is seed-if-empty on
// form_sections — it has already run on every deployed environment and would otherwise never write them.
//
// Seed content lives only in the seeder (migrations never INSERT or UPDATE it, so DHSC admins keep ownership of
// the schema through the Form Designer after its first run). The change therefore ships as the established
// one-shot cleanup: clear the three form-schema tables so the guard re-arms, and the seeder rebuilds the whole
// canonical set — now including the Joint partnership section — on the next boot. Runs exactly once per database
// (__EFMigrationsHistory), never on every boot.
//
// The category and its activity type are NOT seeded here. They arrive through SeedJointPartnershipTaxonomyAsync,
// which guards on the row rather than the table precisely so this migration does not have to clear categories and
// activity_tasks — and with them every activity and submission that references them, which is what the earlier
// taxonomy reseeds (BLUM_6933_ReseedCreateActivitiesProtocol) had to do.
//
// Deliberately narrow, and narrower than BLUM-7759: this only ADDS questions, so no existing answer becomes
// unreachable and there is no activity_answers statement. Activities, submissions, categories, activity_tasks and
// key_result_* are all untouched. The one cost is any section or question a DHSC admin authored through the Form
// Designer before this deploy — the guard needs form_sections empty, and the seeder only rebuilds the canonical
// set (none expected at MVP; called out in the PR).
//
// Children are deleted before parents (option -> question -> section FK).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260907120000_BLUM_7860_ReseedJointPartnershipSection")]
public partial class BLUM_7860_ReseedJointPartnershipSection : Migration
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
        // Nothing to undo — the deleted rows are seed content, and only the seeder writes them. Rolling back does
        // not remove the Joint partnership section: by the time anything could run Down, Up's boot has already
        // repopulated the schema and the seed-if-empty guard is armed again, so redeploying the previous app
        // version rebuilds nothing. The category itself survives a rollback too, and is harmless there — the old
        // client simply renders it with no detail section.
    }
}