using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_7571_CollectionReminderDispatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_reminder_dispatches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_type = table.Column<string>(type: "varchar(32)", nullable: false),
                    scheduled_for = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    offset_type = table.Column<string>(type: "varchar(32)", nullable: true),
                    days = table.Column<int>(type: "integer", nullable: true),
                    outstanding_organisation_count = table.Column<int>(type: "integer", nullable: false),
                    provider_emails_sent = table.Column<int>(type: "integer", nullable: false),
                    dhsc_emails_sent = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_reminder_dispatches", x => x.id);
                    table.ForeignKey(
                        name: "fk_collection_reminder_dispatches_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_reminder_dispatches_collection_id",
                table: "collection_reminder_dispatches",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_reminder_dispatches_collection_id_trigger_type_s",
                table: "collection_reminder_dispatches",
                columns: new[] { "collection_id", "trigger_type", "scheduled_for" },
                unique: true,
                filter: "trigger_type <> 'manual'");

            // Backfill (BLUM-7571): every trigger point that has ALREADY passed when this ships is claimed, so
            // the first sweep after deploy does not mail every outstanding organisation once per historical fire
            // date. Without it the table starts empty, and every open window whose latest reminder date falls
            // inside the catch-up window (MaxReminderNotificationAgeDays, 7 days) fans out on deploy day.
            //
            // sent_at stays NULL because nothing was actually sent: these rows suppress a send, they do not
            // record one. The counts are 0 for the same reason.
            //
            // The offsets are restated here in SQL because ReminderSchedule is C#, and the arms mirror its
            // filters: a fire date must fall inside the window (>= open, <= deadline) to be a real trigger
            // point. The <= today arm is essential — claiming a FUTURE fire date would permanently suppress a
            // legitimate upcoming reminder.
            //
            // "today" is anchored to UTC rather than written as current_date, for the reason the BLUM-7719
            // backfill records: collection_open_date and submission_deadline are timestamptz holding UTC
            // midnight (DateOnlyConverter) while current_date resolves in the connection's TimeZone, so on a
            // non-UTC session the two disagree about the boundary day — and ReminderSchedule, which is pure
            // UTC, would then see a fire date this backfill left unclaimed.
            //
            // distinct on mirrors ReminderSchedule.DueOn's de-duplication: two configured reminders can resolve
            // to the same calendar day, and the unique index would reject the second row.
            migrationBuilder.Sql(
                """
                insert into collection_reminder_dispatches (
                    id, collection_id, trigger_type, scheduled_for, offset_type, days,
                    outstanding_organisation_count, provider_emails_sent, dhsc_emails_sent, created_at, sent_at)
                select distinct on (c.id, f.fire_date)
                       gen_random_uuid(), c.id, 'scheduled', f.fire_date, r.offset_type, r.days,
                       0, 0, 0, now(), null
                  from collections c
                  join collection_reminders r on r.collection_id = c.id
                 cross join lateral (
                       select case when r.offset_type = 'after_open'
                                   then c.collection_open_date + (r.days * interval '1 day')
                                   else c.submission_deadline  - (r.days * interval '1 day')
                              end as fire_date
                 ) f
                 where c.deleted_at is null
                   and c.is_draft = false
                   and c.is_manually_closed = false
                   and c.collection_open_date is not null
                   and c.submission_deadline is not null
                   and f.fire_date >= c.collection_open_date
                   and f.fire_date <= c.submission_deadline
                   and f.fire_date <= timezone('UTC', date_trunc('day', now() at time zone 'UTC'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_reminder_dispatches");
        }
    }
}