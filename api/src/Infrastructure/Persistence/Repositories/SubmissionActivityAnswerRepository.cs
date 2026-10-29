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

        await SaveDetachingOnFailureAsync(answers, cancellationToken);
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

        await SaveDetachingOnFailureAsync(answers, cancellationToken);
    }

    // Left tracked as Added, refused rows would be retried by the caller's next save and fail that too. The
    // confirm-time carry-forward swallows its own failure and the section recompute saves straight after it, so a
    // refused carried answer answered the confirmation with a 500 — the AuditLogRepository.AppendAsync and
    // MaturityAssessmentAnswerRepository detach, for the same reason. The delete above is not undone here: outside a
    // transaction it has already committed, which is the existing contract of a replace.
    private async Task SaveDetachingOnFailureAsync(IReadOnlyList<SubmissionActivityAnswer> answers, CancellationToken cancellationToken)
    {
        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var answer in answers)
            {
                Context.Entry(answer).State = EntityState.Detached;
            }

            throw;
        }
    }
}