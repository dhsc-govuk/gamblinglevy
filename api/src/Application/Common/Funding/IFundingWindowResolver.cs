using Domain.Funding;

namespace Application.Common.Funding;

public interface IFundingWindowResolver
{
    Task<FundingWindow> ResolveCurrentAsync(CancellationToken cancellationToken = default);

    // Finds (or creates) the window for an EXPLICIT financial year, rather than deriving it from the current date.
    // A report is always about its own collection's financial year, which can differ from "now" once the real
    // calendar rolls past 6 April while a late report for the prior year is still being viewed or submitted.
    Task<FundingWindow> ResolveForFinancialYearAsync(string financialYear, CancellationToken cancellationToken = default);
}