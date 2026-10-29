using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.Organisations;

namespace Infrastructure.Persistence.Repositories;

// Cumulative levy spend an organisation has reported for a financial year: the sum of Actual spend across the org's
// submissions whose collection falls in that year. Replaces SpendReportTotalsStub so the funding dashboard's
// "Spent to date" / "Remaining budget" reflect real "Record funding spent" data (BLUM-6612).
public class SpendReportTotals(
    ISubmissionQueries submissionQueries,
    ISubmissionSpendEntryRepository spendEntryRepository) : ISpendReportTotals
{
    public async Task<decimal> GetTotalForFinancialYearAsync(
        OrganisationId organisationId,
        string financialYear,
        CancellationToken cancellationToken = default)
    {
        var submissions = await submissionQueries.GetForOrgAndFinancialYearAsync(
            organisationId, financialYear, cancellationToken);

        var submissionIds = submissions.Select(s => s.SubmissionId).ToList();
        if (submissionIds.Count == 0)
        {
            return 0m;
        }

        return await spendEntryRepository.SumActualForSubmissionsAsync(submissionIds, cancellationToken);
    }
}