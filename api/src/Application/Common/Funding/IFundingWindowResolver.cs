using Domain.Funding;

namespace Application.Common.Funding;

public interface IFundingWindowResolver
{
    Task<FundingWindow> ResolveCurrentAsync(CancellationToken cancellationToken = default);
}