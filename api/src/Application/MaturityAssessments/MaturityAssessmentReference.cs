using System.Globalization;

namespace Application.MaturityAssessments;

// Builds the human-facing maturity-assessment reference number shown on the confirmation screen:
//   {FY-end-year}-{sequence:0000}
// e.g. financial year 2026/27, the 197th assessment submitted for that year => "2027-0197" (the Figma example).
// The FY end year is the financial-year start year + 1, matching SubmissionReference; there is no quarter segment
// because the assessment is annual. Pure + isolated like ReportingPeriod.
public static class MaturityAssessmentReference
{
    public static string Format(string financialYear, int sequence)
    {
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence must be 1 or greater.");
        }

        var endYear = ParseLeadingYear(financialYear) + 1;
        return $"{endYear:0000}-{sequence:0000}";
    }

    // The financial-year end year on its own — the client names the assessment "Maturity assessment for {end year}".
    public static int EndYear(string financialYear) => ParseLeadingYear(financialYear) + 1;

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