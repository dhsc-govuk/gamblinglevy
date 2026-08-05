using System.Globalization;

namespace Application.Collections;

// Pure, isolated, unit-testable helper that derives a reporting period (Start/End) from a financial-year string
// and an optional UK fiscal quarter. Kept free of domain/EF dependencies so it can be unit-tested in isolation.
//
// NOTE: The financial-year string format ("2026" or "2026/27") and the quarter -> date mapping below are an
// ASSUMPTION to confirm with design. The Figma collections list implies UK fiscal quarters of a financial year
// starting 1 April, with each quarter's reporting period ending on the quarter end (and the submission deadline
// implied to be that quarter end). If design later defines a different FY notation or quarter boundaries, update
// this single helper.
public static class ReportingPeriod
{
    public static (DateOnly Start, DateOnly End) Derive(string financialYear, int? quarter)
    {
        var startYear = ParseLeadingYear(financialYear);

        return quarter switch
        {
            // UK fiscal quarters of the financial year starting 1 April of startYear.
            1 => (new DateOnly(startYear, 4, 1), new DateOnly(startYear, 6, 30)),
            2 => (new DateOnly(startYear, 7, 1), new DateOnly(startYear, 9, 30)),
            3 => (new DateOnly(startYear, 10, 1), new DateOnly(startYear, 12, 31)),
            4 => (new DateOnly(startYear + 1, 1, 1), new DateOnly(startYear + 1, 3, 31)),

            // No quarter (e.g. maturity assessment): the full financial year, 1 Apr Y .. 31 Mar (Y+1).
            null => (new DateOnly(startYear, 4, 1), new DateOnly(startYear + 1, 3, 31)),

            _ => throw new ArgumentOutOfRangeException(nameof(quarter), quarter, "Quarter must be between 1 and 4, or null."),
        };
    }

    // The quarters still to be forecast: the quarters strictly AFTER the reporting quarter, to the end of the
    // financial year (e.g. currentQuarter 2 -> [3, 4]; currentQuarter 4 -> [] — nothing left to forecast). The
    // reporting quarter itself is captured as actual spend, not forecast, so it is excluded here. Drives the
    // "Forecast spend" screen (which is skipped entirely when this is empty).
    public static IReadOnlyList<int> RemainingQuarters(int currentQuarter)
    {
        if (currentQuarter is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(currentQuarter), currentQuarter, "Quarter must be between 1 and 4.");
        }

        return Enumerable.Range(currentQuarter + 1, 4 - currentQuarter).ToList();
    }

    // Parses the leading 4-digit year from a financial-year string. Accepts "2026" and "2026/27" (and similar),
    // taking the first four digits as the year the financial year starts.
    private static int ParseLeadingYear(string financialYear)
    {
        if (string.IsNullOrWhiteSpace(financialYear))
        {
            throw new ArgumentException("Financial year must not be empty.", nameof(financialYear));
        }

        var trimmed = financialYear.Trim();
        var digits = new string(trimmed.TakeWhile(char.IsDigit).ToArray());

        if (digits.Length < 4 || !int.TryParse(digits.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            throw new ArgumentException($"Financial year \"{financialYear}\" must start with a 4-digit year.", nameof(financialYear));
        }

        return year;
    }
}