using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_8155_CollectionWindowClosedLaNotified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "window_closed_la_notified_at",
                table: "collections",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill (BLUM-8155): every window that is ALREADY closed when this ships is stamped as notified,
            // so the first sweep after deploy does not retroactively mail every non-submitting LA once per
            // historical collection. Same predicate and UTC anchoring as the BLUM-7719 closure-notification
            // backfill — see that migration for why the deadline comparison can't be written as current_date.
            migrationBuilder.Sql(
                """
                update collections
                   set window_closed_la_notified_at = now()
                 where deleted_at is null
                   and is_draft = false
                   and (is_manually_closed = true
                        or submission_deadline < timezone('UTC', date_trunc('day', now() at time zone 'UTC')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "window_closed_la_notified_at",
                table: "collections");
        }
    }
}