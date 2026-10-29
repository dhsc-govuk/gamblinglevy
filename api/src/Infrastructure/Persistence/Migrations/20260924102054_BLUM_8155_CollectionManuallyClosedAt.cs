using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BLUM_8155_CollectionManuallyClosedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "manually_closed_at",
                table: "collections",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: an already-manually-closed collection has no recorded close instant (Close() only ever set
            // the IsManuallyClosed flag before this column existed), so BLUM-8155's GetClosedOnDate() would read a
            // false null. updated_at is the honest stand-in — Close() sets it, and EnsureEditable() blocks every
            // further edit once a window is Closed, so for a row where is_manually_closed is true, updated_at is
            // exactly the Close() timestamp (the closure-notified/window-closed-la-notified markers deliberately
            // never touch it either, per Collection.MarkClosureNotified()'s own comment).
            migrationBuilder.Sql(
                """
                update collections
                   set manually_closed_at = updated_at
                 where is_manually_closed = true
                   and manually_closed_at is null;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "manually_closed_at",
                table: "collections");
        }
    }
}