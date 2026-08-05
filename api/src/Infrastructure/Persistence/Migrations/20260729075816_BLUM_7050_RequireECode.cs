using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    // BLUM-7050 makes the E-code mandatory on every write path. Organisations created before the rule carry no
    // code, so they are backfilled with the "TBC" sentinel before the column is tightened — without that the
    // ALTER fails on the first NULL row, and enforcing the rule only in the validators would leave those
    // organisations un-editable (every save re-runs the rule against a field the admin has never seen).
    //
    // "TBC" is deliberately not a plausible finance identifier: it appears verbatim in the E-code column of the
    // DHSC cross-organisation report export, so it has to read as "not supplied yet" to whoever opens that CSV
    // rather than pass for a real code. It is greppable as a single literal for the follow-up that replaces
    // these with the codes DHSC finance actually holds.

    /// <inheritdoc />
    public partial class BLUM_7050_RequireECode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Also sweeps up whitespace-only values. Nothing writes those today — the validators trim and
            // reject empties — but the column has been nullable free text since BLUM-6008, so a blank is
            // possible in existing data and would survive a NULL-only backfill straight into the alter below.
            //
            // The trim set is explicit because single-argument btrim() strips ASCII space ONLY: a value that is
            // a tab, newline, carriage return, form feed, vertical tab or non-breaking space would otherwise
            // pass through unsentinelled into a NOT NULL column, while C#'s string.IsNullOrWhiteSpace — used by
            // the Guard and both validators — still considers it blank. The database and the application would
            // then disagree about whether that organisation has an E-code, and the follow-up clean-up (which
            // finds rows by grepping for the literal 'TBC') would skip it silently.
            migrationBuilder.Sql(
                """
                UPDATE organisations
                SET e_code = 'TBC'
                WHERE e_code IS NULL
                   OR btrim(e_code, E' \t\n\r\f\x0B\u00A0') = '';
                """);

            // EF scaffolded this with defaultValue: "" to fill the NULLs it assumed were still there. The
            // backfill above leaves none, and a column-level DEFAULT '' would quietly re-admit the empty E-code
            // this migration exists to eliminate — so it is dropped deliberately, not by oversight.
            migrationBuilder.AlterColumn<string>(
                name: "e_code",
                table: "organisations",
                type: "varchar(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "e_code",
                table: "organisations",
                type: "varchar(64)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)");

            // Deliberately no data rollback. An earlier version of this method nulled every row holding 'TBC',
            // which cannot tell a row this migration backfilled from one an admin genuinely typed 'TBC' into —
            // the form accepts it, because NotEmpty passes. Rolling back would then destroy a real answer, and
            // re-applying Up() could not tell the two apart either, so the loss would be silent (BLUM-7050
            // review).
            //
            // Down()'s job here is to reverse the SCHEMA change, which the AlterColumn above does. Leaving the
            // sentinel in place is lossless and safe: the column is nullable again so 'TBC' is a legal value,
            // and re-running Up() is idempotent because 'TBC' does not match the blank predicate. Clearing the
            // placeholders is the follow-up that replaces them with real DHSC finance codes, not a rollback.
        }
    }
}