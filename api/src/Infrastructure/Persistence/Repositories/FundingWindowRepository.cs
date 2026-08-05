using Application.Common.Interfaces.Repositories;
using Domain.Funding;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class FundingWindowRepository(ApplicationDbContext dbContext)
    : RepositoryBase<FundingWindow, FundingWindowId>(dbContext), IFundingWindowRepository
{
    public async Task<Option<FundingWindow>> FindByFinancialYearAsync(string financialYear, CancellationToken cancellationToken = default)
    {
        var entity = await Context.FundingWindows.AsNoTracking().FirstOrDefaultAsync(x => x.FinancialYear == financialYear, cancellationToken);
        return entity ?? Option<FundingWindow>.None;
    }

    public async Task<Option<FundingWindow>> FindByIdAsync(FundingWindowId id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.FundingWindows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? Option<FundingWindow>.None;
    }
}