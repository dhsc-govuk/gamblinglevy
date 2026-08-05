using Application.Common.Interfaces.Repositories;
using Domain.Activities;
using Domain.Submissions;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class SubmissionActivityAnswerRepository(ApplicationDbContext dbContext)
    : RepositoryBase<SubmissionActivityAnswer, SubmissionActivityAnswerId>(dbContext), ISubmissionActivityAnswerRepository
{
    public async Task<IReadOnlyList<SubmissionActivityAnswer>> GetForActivityAsync(
        SubmissionId submissionId,
        ActivityId activityId,
        CancellationToken cancellationToken = default)
    {
        return await Context.SubmissionActivityAnswers
            .AsNoTracking()
            .Where(a => a.SubmissionId == submissionId && a.ActivityId == activityId)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceForActivityAsync(
        SubmissionId submissionId,
        ActivityId activityId,
        IReadOnlyList<SubmissionActivityAnswer> answers,
        CancellationToken cancellationToken = default)
    {
        await Context.SubmissionActivityAnswers
            .Where(a => a.SubmissionId == submissionId && a.ActivityId == activityId)
            .ExecuteDeleteAsync(cancellationToken);

        if (answers.Count > 0)
        {
            await Context.SubmissionActivityAnswers.AddRangeAsync(answers, cancellationToken);
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceForQuestionsAsync(
        SubmissionId submissionId,
        ActivityId activityId,
        IReadOnlyList<string> questionCodes,
        IReadOnlyList<SubmissionActivityAnswer> answers,
        CancellationToken cancellationToken = default)
    {
        var codes = questionCodes.ToList();
        if (codes.Count > 0)
        {
            await Context.SubmissionActivityAnswers
                .Where(a => a.SubmissionId == submissionId && a.ActivityId == activityId && codes.Contains(a.QuestionCode))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (answers.Count > 0)
        {
            await Context.SubmissionActivityAnswers.AddRangeAsync(answers, cancellationToken);
        }

        await Context.SaveChangesAsync(cancellationToken);
    }
}