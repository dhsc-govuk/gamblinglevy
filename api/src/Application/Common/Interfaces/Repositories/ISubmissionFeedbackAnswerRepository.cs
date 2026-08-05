using Domain.Submissions;

namespace Application.Common.Interfaces.Repositories;

public interface ISubmissionFeedbackAnswerRepository
{
    Task<IReadOnlyList<SubmissionFeedbackAnswer>> GetForSubmissionAsync(SubmissionId submissionId, CancellationToken cancellationToken = default);

    // Replaces the answers for only the given question codes (delete-then-insert), leaving every other answer intact.
    // Each feedback page submits its own question codes, so saving one page never wipes another's answers — the same
    // partitioned-replace idea the funding screens use per spend kind. `answers` holds the non-empty values to insert.
    Task ReplaceForQuestionsAsync(SubmissionId submissionId, IReadOnlyList<string> questionCodes, IReadOnlyList<SubmissionFeedbackAnswer> answers, CancellationToken cancellationToken = default);
}