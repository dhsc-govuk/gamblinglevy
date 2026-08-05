using System.Globalization;

namespace Application.Submissions;

// Builds the human-facing quarterly-report reference number shown on the confirmation screen:
//   {FY-end-year}{quarter:00}-{sequence:0000}
// e.g. financial year 2026/27, quarter 1, the 197th submission for that collection => "202701-0197".
// The FY end year is the financial-year start year + 1 (a UK financial year starting 1 April of year Y ends in
// Y+1), so every quarter of FY 2026/27 shares the prefix 2027. Pure + isolated like Application.Collections.ReportingPeriod.
public static class SubmissionReference
{
    public static string Format(string financialYear, int quarter, int sequence)
    {
        if (quarter is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(quarter), quarter, "Quarter must be between 1 and 4.");
        }

        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence must be 1 or greater.");
        }

        var endYear = ParseLeadingYear(financialYear) + 1;
        return $"{endYear:0000}{quarter:00}-{sequence:0000}";
    }

    // Parses the leading 4-digit year from a financial-year string ("2026" or "2026/27"). Mirrors ReportingPeriod's
    // parsing so the two helpers stay independent.
    private static int ParseLeadingYear(string financialYear)
    {
        if (string.IsNullOrWhiteSpace(financialYear))
        {
            throw new ArgumentException("Financial year must not be empty.", nameof(financialYear));
        }

        var digits = new string(financialYear.Trim().TakeWhile(char.IsDigit).ToArray());

        if (digits.Length < 4 || !int.TryParse(digits.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            throw new ArgumentException($"Financial year \"{financialYear}\" must start with a 4-digit year.", nameof(financialYear));
        }

        return year;
    }
}