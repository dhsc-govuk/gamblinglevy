using Application.Common.Funding;
using Application.Common.Interfaces.Repositories;
using Domain.Funding;

namespace Infrastructure.Funding;

public class FundingWindowResolver(IFundingWindowRepository repository) : IFundingWindowResolver
{
    public async Task<FundingWindow> ResolveCurrentAsync(CancellationToken cancellationToken = default)
    {
        var financialYear = CurrentFinancialYear(DateTime.UtcNow);
        var existing = await repository.FindByFinancialYearAsync(financialYear, cancellationToken);

        if (existing.IsSome)
        {
            return existing.Match(x => x, () => throw new InvalidOperationException("unreachable"));
        }

        var label = $"Annual Allocation {financialYear}";
        var window = FundingWindow.New(FundingWindowId.New(), financialYear, label);
        return await repository.AddAsync(window, cancellationToken);
    }

    internal static string CurrentFinancialYear(DateTime nowUtc)
    {
        // UK financial year runs 6 April – 5 April. Pre-6-April dates belong to the previous calendar year's FY.
        var fyStartYear = (nowUtc.Month > 4 || (nowUtc.Month == 4 && nowUtc.Day >= 6))
            ? nowUtc.Year
            : nowUtc.Year - 1;
        var fyEndYear = (fyStartYear + 1) % 100;
        return $"{fyStartYear}/{fyEndYear:D2}";
    }
}