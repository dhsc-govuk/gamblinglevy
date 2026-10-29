using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Domain.Funding;

namespace Infrastructure.Funding;

public class FundingWindowResolver(IFundingWindowRepository repository, IDateTimeProvider dateTimeProvider) : IFundingWindowResolver
{
    public Task<FundingWindow> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
        ResolveForFinancialYearAsync(FinancialYearRange.Current(dateTimeProvider.GetUtcNow()), cancellationToken);

    public async Task<FundingWindow> ResolveForFinancialYearAsync(string financialYear, CancellationToken cancellationToken = default)
    {
        var existing = await repository.FindByFinancialYearAsync(financialYear, cancellationToken);

        if (existing.IsSome)
        {
            return existing.Match(x => x, () => throw new InvalidOperationException("unreachable"));
        }

        var label = $"Annual Allocation {financialYear}";
        var window = FundingWindow.New(FundingWindowId.New(), financialYear, label, dateTimeProvider.GetUtcNow());
        return await repository.AddAsync(window, cancellationToken);
    }
}