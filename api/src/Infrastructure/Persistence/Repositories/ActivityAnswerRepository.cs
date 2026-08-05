using Application.Common.Interfaces.Repositories;
using Domain.Activities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class ActivityAnswerRepository(ApplicationDbContext dbContext)
    : RepositoryBase<ActivityAnswer, ActivityAnswerId>(dbContext), IActivityAnswerRepository
{
    public async Task<IReadOnlyList<ActivityAnswer>> GetForActivityAsync(
        ActivityId activityId,
        CancellationToken cancellationToken = default)
    {
        return await Context.ActivityAnswers
            .AsNoTracking()
            .Where(x => x.ActivityId == activityId)
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveByActivityAsync(
        ActivityId activityId,
        IReadOnlyCollection<string> questionCodes,
        CancellationToken cancellationToken = default)
    {
        await Context.ActivityAnswers
            .Where(x => x.ActivityId == activityId && questionCodes.Contains(x.QuestionCode))
            .ExecuteDeleteAsync(cancellationToken);
    }
}