using Application.Common.Interfaces.Repositories;

namespace Application.Common.Funding;

/// <summary>
/// Picks the two allocations that describe an organisation for a given financial year, out of everything it
/// holds (BLUM-7608).
///
/// An organisation accumulates one row per financial year - amending 2026/27 does not overwrite 2025/26,
/// because that older figure is what every submission frozen in 2025/26 was measured against. The screens still
/// show exactly two figures and a report is still measured against exactly one, so something has to decide
/// which rows those are.
///
/// This replaced IOrgFundingAllocationRepository.FindLatestForOrganisationAsync at every "the organisation's
/// current allocation" call site. That method orders by CreatedAt, which was unambiguous while an organisation
/// had a single row but silently became "the row written last" once create and amend could write two in one
/// request - i.e. the UPCOMING year. Every caller that kept it would have amended, audited, or reported against
/// next year's money.
/// </summary>
public static class AllocationSelector
{
    /// <summary>
    /// The allocations describing the financial year in progress.
    /// </summary>
    public static (OrgAllocationWithWindow? Current, OrgAllocationWithWindow? Next) Select(
        IEnumerable<OrgAllocationWithWindow> allocations,
        DateTime nowUtc) =>
        SelectForFinancialYear(allocations, FinancialYearRange.Current(nowUtc));

    /// <summary>
    /// "Current" is the row for <paramref name="financialYear"/> itself; failing that the LATEST row for an
    /// earlier year; failing that the EARLIEST row for a later one. That order matters in both directions:
    ///
    /// - Preferring an earlier year over a later one is what stops a report for 2025/26 being measured against
    ///   a 2027/28 allocation that had not been agreed when it was authored. It is also what keeps every
    ///   organisation created before this ticket working, since each holds a single row windowed to whatever
    ///   year was current at onboarding - the shape the bulk import still produces.
    /// - Falling forward when there is nothing earlier is what stops an organisation onboarded in March, whose
    ///   only allocation is for the year about to start, showing nothing at all.
    ///
    /// "Next" is the row for the year immediately after whichever year "current" resolved to - not simply the
    /// second-newest row, so a gap in the middle reads as a gap rather than as next year's money.
    /// </summary>
    public static (OrgAllocationWithWindow? Current, OrgAllocationWithWindow? Next) SelectForFinancialYear(
        IEnumerable<OrgAllocationWithWindow> allocations,
        string financialYear)
    {
        // Ordinal on "YYYY/YY" sorts chronologically, which is the only reason the year is stored in that shape.
        var ordered = allocations
            .OrderBy(x => x.Window.FinancialYear, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0)
        {
            return (null, null);
        }

        var current = ordered.LastOrDefault(x => string.CompareOrdinal(x.Window.FinancialYear, financialYear) <= 0)
                      ?? ordered[0];

        var next = FinancialYearRange.IsValid(current.Window.FinancialYear)
            ? ordered.FirstOrDefault(x => x.Window.FinancialYear == FinancialYearRange.Next(current.Window.FinancialYear))
            : null;

        return (current, next);
    }
}