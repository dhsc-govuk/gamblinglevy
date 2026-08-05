using Application.Common.Interfaces.Repositories;
using Domain.Activities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class ActivityExpectedResultRepository(ApplicationDbContext dbContext)
    : RepositoryBase<ActivityExpectedResult, ActivityExpectedResultId>(dbContext), IActivityExpectedResultRepository
{
    public async Task<IReadOnlyList<ActivityExpectedResult>> GetForActivityAsync(
        ActivityId activityId,
        CancellationToken cancellationToken = default)
    {
        return await Context.ActivityExpectedResults
            .AsNoTracking()
            .Where(x => x.ActivityId == activityId)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
    }
}