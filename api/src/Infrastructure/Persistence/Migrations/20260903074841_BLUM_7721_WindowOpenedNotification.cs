using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    // BLUM-7721: the reporting window opening now emails every contact at every organisation assigned to the
    // collection, plus every DHSC Super Admin. It rides on BLUM-7571's collection_reminder_dispatches claim table
    // with a new trigger_type of 'window_opened' — which needs no DDL of its own, the column being varchar(32)
    // with no check constraint. The two things that DO need a migration are here.
    //
    // ONE: the once-per-collection unique index. Every other dispatch is claimed per trigger POINT, which is why
    // the existing composite index includes scheduled_for — a reminder resolving to a new calendar date is a new
    // thing to send. "The reporting window has opened" is not like that: it is one fact about one collection,
    // announced once. And collection_open_date is mutable while a window is open — Collection.UpdateDetails only
    // refuses once the collection is Closed, and UpdateCollectionCommand RECOMPUTES the open date from the
    // financial year and quarter whenever window_same_as_reporting_period is true. A date-keyed claim would
    // therefore let an admin correcting an unrelated field re-announce the opening to every Local Authority in
    // the country. This index makes that impossible rather than merely unlikely.
    //
    // TWO: the backfill, which is mandatory. Without it the first sweep after deploy announces every window that
    // opened within MaxReminderNotificationAgeDays (7 days) to the whole estate — for an all-LAs window that is
    // ~153 organisations times their active contacts — about windows everyone already knows opened. The accepted
    // cost, stated so it is a decision rather than a surprise: a window that opened in the day or two before
    // deploy and genuinely was owed an announcement never gets one.
    //
    // The predicate is deliberately BROADER than BLUM-7571's backfill. That one had to reproduce a reminder's
    // fire date and so needed the closed and in-window arms; these rows exist purely to SUPPRESS, so an extra one
    // costs nothing while a missing one costs a mass mailing. Drafts stay excluded — publishing one later
    // genuinely does owe an announcement.
    //
    // timezone('UTC', date_trunc('day', now() at time zone 'UTC')) rather than current_date, for the reason the
    // BLUM-7571 migration documents: DateOnlyConverter stores these as UTC-midnight timestamptz while
    // current_date resolves in the connection's TimeZone, so on a Europe/London session it casts to 23:00Z of the
    // previous day — less than a collection stored as opening today, leaving it unclaimed and mailing the estate
    // on deploy day. collection_open_date is copied into scheduled_for untouched for the same reason: any cast
    // would shift it off the value ReminderSchedule.WindowOpenedOn later computes.
    public partial class BLUM_7721_WindowOpenedNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before the index, not after: one row per collection is inserted, so there is nothing for the
            // constraint to reject, and building it once at the end is cheaper than maintaining it through the
            // insert.
            //
            // sent_at stays NULL on every backfilled row, exactly as BLUM-7571's does. These rows suppress a
            // send; they do not record one.
            migrationBuilder.Sql(
                """
                insert into collection_reminder_dispatches (
                    id, collection_id, trigger_type, scheduled_for, offset_type, days,
                    outstanding_organisation_count, provider_emails_sent, dhsc_emails_sent, created_at, sent_at)
                select gen_random_uuid(), c.id, 'window_opened', c.collection_open_date, null, null,
                       0, 0, 0, now(), null
                  from collections c
                 where c.deleted_at is null
                   and c.is_draft = false
                   and c.collection_open_date is not null
                   and c.collection_open_date <= timezone('UTC', date_trunc('day', now() at time zone 'UTC'));
                """);

            migrationBuilder.CreateIndex(
                name: "ix_collection_reminder_dispatches_window_opened",
                table: "collection_reminder_dispatches",
                column: "collection_id",
                unique: true,
                filter: "trigger_type = 'window_opened'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collection_reminder_dispatches_window_opened",
                table: "collection_reminder_dispatches");

            // The rows go too, and not only the backfilled ones. ReminderTrigger.From THROWS on a value the
            // running build does not know — the same trap CLAUDE.md records for users.role — so a 'window_opened'
            // row left behind after a rollback breaks every read that materialises a CollectionReminderDispatch.
            // Deploy this forward only; if it has to come back out, run Down first.
            migrationBuilder.Sql("delete from collection_reminder_dispatches where trigger_type = 'window_opened';");
        }
    }
}