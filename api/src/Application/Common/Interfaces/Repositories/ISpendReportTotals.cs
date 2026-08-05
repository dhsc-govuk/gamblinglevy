using Domain.Funding;
using Domain.Organisations;

namespace Application.Common.Interfaces.Repositories;

/// Forward-compat total — the SpendReport aggregate does not exist yet. Returns 0 until the
/// Submission / SpendReport domain lands (Data-Schema-Spec §5.3). The funding dashboard (BLUM-6213)
/// computes remaining budget = allocated − this value, so a 0 stub yields remaining == allocated.
public interface ISpendReportTotals
{
    Task<decimal> GetApprovedTotalForWindowAsync(
        OrganisationId organisationId,
        FundingWindowId fundingWindowId,
        CancellationToken cancellationToken = default);
}