using Domain.Funding;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface IFundingWindowRepository
{
    Task<Option<FundingWindow>> FindByFinancialYearAsync(string financialYear, CancellationToken cancellationToken = default);
    Task<Option<FundingWindow>> FindByIdAsync(FundingWindowId id, CancellationToken cancellationToken = default);
    Task<FundingWindow> AddAsync(FundingWindow entity, CancellationToken cancellationToken = default);
}