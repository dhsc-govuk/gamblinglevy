using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

// BLUM-7126: before this fix nothing stopped an Active activity being saved with a future "When did the work
// start?" date (start_month/start_year). Now that Create/UpdateActivityCommandValidator reject a future start
// date, any row already persisted that way is un-editable — every save of an Active activity re-runs the rule
// against its unchanged future date and fails, even for edits that never touched the date. This one-shot cleanup
// removes those stranded rows so the new rule can't lock anyone out.
//
// "Remove" = soft-delete (set deleted_at), matching the app's own deletion model: every activity read path filters
// deleted_at IS NULL (ActivityRepository), so a soft-deleted activity disappears from lists and the edit flow, and
// the child tables (activity_answers, activity_expected_results) plus the submission tables reference activity ids
// with NO FK constraint, so a hard DELETE would only orphan those rows. deleted_by is left NULL to denote a system
// action (the column is nullable). The activity remains in the DHSC "deleted activities" restore list.
//
// Scope is deliberately narrow: only status = 'active', not-already-deleted rows whose (start_year, start_month)
// is strictly later than the current UTC month — the exact condition the validators enforce (ActivityStartDate
// .IsInFuture, which uses DateTime.UtcNow). Current-month and past dates, drafts, and non-active activities are
// untouched. Runs exactly once per database (__EFMigrationsHistory).
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723120000_BLUM_7126_SoftDeleteFutureDatedActivities")]
public partial class BLUM_7126_SoftDeleteFutureDatedActivities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE activities
            SET deleted_at = now(),
                updated_at = now()
            WHERE deleted_at IS NULL
              AND status = 'active'
              AND start_year IS NOT NULL
              AND start_month IS NOT NULL
              AND (
                    start_year > EXTRACT(YEAR FROM (now() AT TIME ZONE 'UTC'))::int
                 OR (
                      start_year = EXTRACT(YEAR FROM (now() AT TIME ZONE 'UTC'))::int
                      AND start_month > EXTRACT(MONTH FROM (now() AT TIME ZONE 'UTC'))::int
                    )
                  );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Not reversible: the soft-deleted rows aren't marked as this migration's doing, so we can't distinguish
        // them from activities a user deleted around the same time. They remain restorable via the app's normal
        // "deleted activities" flow if needed.
    }
}