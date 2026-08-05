using Application.Common.Interfaces.Repositories;
using Domain.Submissions;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class SubmissionFeedbackAnswerRepository(ApplicationDbContext dbContext)
    : RepositoryBase<SubmissionFeedbackAnswer, SubmissionFeedbackAnswerId>(dbContext), ISubmissionFeedbackAnswerRepository
{
    public async Task<IReadOnlyList<SubmissionFeedbackAnswer>> GetForSubmissionAsync(
        SubmissionId submissionId,
        CancellationToken cancellationToken = default)
    {
        return await Context.SubmissionFeedbackAnswers
            .AsNoTracking()
            .Where(a => a.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceForQuestionsAsync(
        SubmissionId submissionId,
        IReadOnlyList<string> questionCodes,
        IReadOnlyList<SubmissionFeedbackAnswer> answers,
        CancellationToken cancellationToken = default)
    {
        var codes = questionCodes.ToList();
        if (codes.Count > 0)
        {
            await Context.SubmissionFeedbackAnswers
                .Where(a => a.SubmissionId == submissionId && codes.Contains(a.QuestionCode))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (answers.Count > 0)
        {
            await Context.SubmissionFeedbackAnswers.AddRangeAsync(answers, cancellationToken);
        }

        await Context.SaveChangesAsync(cancellationToken);
    }
}