using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_7719_CollectionClosureNotified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "closure_notified_at",
                table: "collections",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill (BLUM-7719): every window that is ALREADY closed when this ships is stamped as notified,
            // so the first sweep after deploy does not mail every DHSC admin once per historical collection.
            // Closure is derived rather than stored, so the predicate has to restate Collection.GetStatus()'s
            // Closed arm here in SQL: published, not deleted, and either closed by hand or past its deadline.
            //
            // The deadline comparison is anchored to UTC rather than written as current_date, because
            // submission_deadline is a timestamptz holding UTC midnight (DateOnlyConverter) while
            // current_date resolves in the connection's TimeZone. On a session that is not UTC the two
            // disagree about the boundary day, and GetStatus() — which is pure UTC — would then see a
            // window as closed that this backfill left unstamped, mailing every admin about it.
            migrationBuilder.Sql(
                """
                update collections
                   set closure_notified_at = now()
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
                name: "closure_notified_at",
                table: "collections");
        }
    }
}