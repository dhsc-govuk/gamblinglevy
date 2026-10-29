using System.Globalization;
using Domain.Collections;

namespace Application.Collections;

// The human label for what a collection covers. AC3's "the quarter" and AC4's "quarter (or maturity assessment
// period)" are one slot in the Notify template with two vocabularies, and choosing between them is business
// wording rather than transport — so it lives here with ReportingPeriod, not in the Notify service, which stays a
// dumb key-to-string mapper. Same split as SubmissionConfirmation.QuarterLabel.
//
// Deliberately NOT the "Quarter 1 (1 April 2026 to 30 June 2026)" shape SubmitReportCommand builds: the
// window-open emails carry the period start and end as their own personalisation fields, so folding the dates in
// would print each of them twice.
//
// It also does not call ReportingPeriod.Derive. The persisted ReportingPeriodStart/End are the admin's saved
// answer; Derive is only what the wizard defaults them to.
//
// Pure and free of EF/domain services for the same reason as ReportingPeriod alongside it: a unit test costs
// nothing, an integration test costs a PostgreSQL container.
public static class CollectionPeriodLabel
{
    private const string MaturityAssessment = "Maturity assessment";

    public static string For(Collection collection)
    {
        if (collection is null)
        {
            return MaturityAssessment;
        }

        // Quarter rather than Type is the primary test: Collection.ValidateWindow forces Quarter to null for any
        // non-quarterly type and requires it for a quarterly one, so it is the field that is always populated
        // correctly — whereas Type is nullable and a legacy row can have none at all.
        var stem = collection.Quarter is { } quarter
            ? string.Format(CultureInfo.InvariantCulture, "Quarter {0}", quarter)
            : MaturityAssessment;

        var financialYear = collection.FinancialYear?.Trim();

        return string.IsNullOrEmpty(financialYear) ? stem : $"{stem} {financialYear}";
    }
}