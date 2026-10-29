using Domain.Organisations;

namespace Application.Common.Interfaces.Repositories;

// Cumulative Actual spend an organisation has recorded for a financial year (BLUM-6612), summed live across
// whatever's been entered so far that year regardless of submitted status — draft actuals count, by design (see
// SpendToDate_AccumulatesActualSpendFromEarlierQuartersOfTheSameYear /
// FundingDashboard_CumulativeSpend_ReflectsCompletedFundingSpend).
//
// The year is passed in as the string the collections themselves carry, NOT as a funding-window id (BLUM-7701).
// A window id meant the total was bounded to the year the org's ALLOCATION ROW happens to be windowed to, and an
// allocation row is never re-windowed — an LA onboarded in 2025/26 still holds that single row while reporting in
// 2026/27, so its dashboard summed a year it had no submissions in and "Spent to date" sat at £0 for ever.
public interface ISpendReportTotals
{
    Task<decimal> GetTotalForFinancialYearAsync(
        OrganisationId organisationId,
        string financialYear,
        CancellationToken cancellationToken = default);
}