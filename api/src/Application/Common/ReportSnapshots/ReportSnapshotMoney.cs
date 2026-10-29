using System.Globalization;

namespace Application.Common.ReportSnapshots;

// The money format shared by everything that writes or reads a frozen funding cell. Format and TryParse are exact
// inverses and live together for that reason: ReportSnapshotService renders the cells at submit time, and
// ReportSnapshotCsv reads them back out of the frozen snapshot to total them per organisation (BLUM-7668). The
// snapshot stores rendered strings, not decimals, so those cells are the only record of the amount — a parser that
// disagreed with the renderer would silently drop money out of a subtotal rather than fail.
public static class ReportSnapshotMoney
{
    // Negative amounts render in accounting-parenthesis form — (1234.56) rather than -1234.56 — because
    // CsvWriter.Escape treats any cell starting with '-' as a possible formula-injection attempt and prefixes it
    // with a single quote to force it to text in Excel/Sheets. That guard is correct for free-text answers but
    // would silently turn every genuinely negative money cell (the overspending organisations these export columns
    // exist to surface) into text that drops out of Excel's own sum/sort. Parentheses are a standard accounting
    // negative-number format and never trip the guard.
    //
    // null renders as an EMPTY cell, never "0.00": blank means "not yet knowable" — a future quarter, or no entry at
    // all — and 0.00 means "entries that sum to zero". The two are different answers downstream (BLUM-7130 CSV
    // review).
    public static string Format(decimal? amount) => amount switch
    {
        null => string.Empty,
        < 0 => $"({Math.Abs(amount.Value).ToString("0.00", CultureInfo.InvariantCulture)})",
        _ => amount.Value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    // The inverse of Format. AllowParentheses is what reads "(150.00)" back as -150.00; the Thousands flag that
    // comes with Number costs nothing and tolerates a separator from a cell rendered by some older format. An empty
    // cell — and any cell that is not a number at all — is "no value": false rather than zero, so a caller can tell
    // "nothing was recorded here" apart from "zero was recorded here".
    public static bool TryParse(string? cell, out decimal amount) =>
        decimal.TryParse(
            cell,
            NumberStyles.Number | NumberStyles.AllowParentheses,
            CultureInfo.InvariantCulture,
            out amount);
}