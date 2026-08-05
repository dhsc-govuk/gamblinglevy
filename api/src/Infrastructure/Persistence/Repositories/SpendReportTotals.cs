using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.Funding;
using Domain.Organisations;
using LanguageExt.UnsafeValueAccess;

namespace Infrastructure.Persistence.Repositories;

// Cumulative levy spend an organisation has reported for a funding window: the sum of Actual spend across the org's
// submissions whose collection falls in the window's financial year. Replaces SpendReportTotalsStub so the funding
// dashboard's "Spent to date" / "Remaining budget" reflect real "Record funding spent" data (BLUM-6612).
public class SpendReportTotals(
    IFundingWindowRepository fundingWindowRepository,
    ISubmissionQueries submissionQueries,
    ISubmissionSpendEntryRepository spendEntryRepository) : ISpendReportTotals
{
    public async Task<decimal> GetApprovedTotalForWindowAsync(
        OrganisationId organisationId,
        FundingWindowId fundingWindowId,
        CancellationToken cancellationToken = default)
    {
        var window = await fundingWindowRepository.FindByIdAsync(fundingWindowId, cancellationToken);
        if (window.IsNone)
        {
            return 0m;
        }

        var submissions = await submissionQueries.GetForOrgAndFinancialYearAsync(
            organisationId, window.ValueUnsafe().FinancialYear, cancellationToken);
        if (submissions.Count == 0)
        {
            return 0m;
        }

        return await spendEntryRepository.SumActualForSubmissionsAsync(
            submissions.Select(s => s.SubmissionId).ToList(), cancellationToken);
    }
}