using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-8340: DHSC has withdrawn "Was this activity shared with other local authorities, or will it be shared?"
// (protocol 1.BCDEF.1.4) from the Activity details step. Strategy, Licensing, Education and Help asked it as
// {ST,LIC,EDU,HELP}_SHARED; Needs assessment lost it in BLUM-7759. SeedFormDesigner no longer seeds it, but that
// seeder is seed-if-empty, so this migration takes it off an already-seeded database.
//
// It only updates rows. The service is live, so it deletes nothing. BLUM-7759 and BLUM-7871 removed the sibling
// questions by clearing form_question_options/form_questions/form_sections and letting the seeder rebuild them;
// that is no longer allowed. Instead the four questions are deactivated:
//   • is_active is what every LA-facing read honours. FormSchemaQueries.GetActiveSchema* filters on it, and that
//     feeds activities/form-schema, CreateActivityCommand/UpdateActivityCommand (so ActivityAnswerValidator never
//     sees the question, and cannot demand it) and the activity-record CSV. The client filters on isActive too.
//   • Keyed on code, which is unique and immutable, so a label a DHSC admin has edited is still caught. The
//     is_active = TRUE guard makes a second run a no-op.
//   • Ids, options and is_required are kept. The Form Designer still lists the questions, tagged "Hidden", and an
//     admin can switch them back on there.
//
// activity_answers rows for these codes are KEPT (BLUM-7759/7871 deleted theirs). Nothing reads them: report
// prefill matches only _WHO_WITH/_WHO_WITH_OTHER, and the View, Check your answers and CSV all walk the active
// schema. UpdateActivityCommand only replaces the codes it is sent or that are applicable, so an edit never
// clears them either. GET activities/{id} still returns them in its raw answers list, which the client ignores.
//
// No deploy window: unlike the reseed migrations, the schema is never empty, so a rolling restart is fine. A tab
// that fetched the schema before the deploy may still show the question for a few minutes; the server ignores the
// submitted value, so the save still succeeds.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260925120000_BLUM_8340_RemoveActivitySharedQuestion")]
public partial class BLUM_8340_RemoveActivitySharedQuestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE form_questions
            SET is_active = FALSE, updated_at = now()
            WHERE code IN ('ST_SHARED', 'LIC_SHARED', 'EDU_SHARED', 'HELP_SHARED') AND is_active = TRUE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Cannot tell a row Up deactivated from one an admin had already hidden before this deploy; both come back.
        migrationBuilder.Sql(
            """
            UPDATE form_questions
            SET is_active = TRUE, updated_at = now()
            WHERE code IN ('ST_SHARED', 'LIC_SHARED', 'EDU_SHARED', 'HELP_SHARED') AND is_active = FALSE;
            """);
    }
}