using Application.Common.Funding;
using Application.MaturityAssessments;
using Application.Submissions;

namespace Tests.Common;

// The financial year a test should use, and the values the app derives from one.
//
// Fixtures used to hardcode "2026/27", which was the current year when they were written. A fixed year silently
// becomes a PAST year, and past years are no longer inert: the per-activity "Total spent so far" figure accumulates
// spend from earlier financial years (BLUM-7703), so a fixture pinned to a stale year changes behaviour on a date
// rather than on a commit. Everything here reads the clock instead, and delegates the FORMAT to the production code
// that owns it, so a test can never disagree with the app about what year it is.
//
// A test that genuinely needs a specific year still passes one — see DhscSubmissionExportTests, which fixes its year
// deliberately and says so.
public static class TestFinancialYears
{
    // Read ONCE per process, not per call. A suite that straddles midnight on 31 March would otherwise seed two
    // collections in a single test against different years, and quietly exercise the cross-year carry-forward the
    // test never asked for.
    public static readonly string Current = FinancialYearRange.Current(DateTime.UtcNow);

    public static string Next(string financialYear) => FinancialYearRange.Next(financialYear);

    public static string Previous(string financialYear) => FinancialYearRange.Previous(financialYear);

    // The reference number a report for this period will be given, built by the production formatter so no test
    // re-implements it. That would be circular if this were the only thing pinning the format — it is not:
    // Application.Tests.Unit/Submissions/SubmissionReferenceTests pins "{FY end year}{quarter:00}-{sequence:0000}"
    // against literals, which is what makes deriving it here safe.
    public static string Reference(string financialYear, int quarter, int sequence) =>
        SubmissionReference.Format(financialYear, quarter, sequence);

    // The maturity assessment's reference ("{FY end year}-{sequence:0000}"), pinned by MaturityAssessmentReferenceTests.
    public static string MaturityReference(string financialYear, int sequence) =>
        MaturityAssessmentReference.Format(financialYear, sequence);
}