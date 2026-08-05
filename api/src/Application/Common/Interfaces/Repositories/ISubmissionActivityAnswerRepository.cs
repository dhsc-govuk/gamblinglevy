using Domain.Activities;
using Domain.Submissions;

namespace Application.Common.Interfaces.Repositories;

public interface ISubmissionActivityAnswerRepository
{
    Task<IReadOnlyList<SubmissionActivityAnswer>> GetForActivityAsync(SubmissionId submissionId, ActivityId activityId, CancellationToken cancellationToken = default);

    // Replaces the activity's answer set wholesale (delete-then-insert). Used at complete time to re-persist the
    // validated set, which prunes orphaned rows (codes that no longer resolve to a question).
    Task ReplaceForActivityAsync(SubmissionId submissionId, ActivityId activityId, IReadOnlyList<SubmissionActivityAnswer> answers, CancellationToken cancellationToken = default);

    // Partial merge for the per-page save contract: deletes only the activity's rows whose code is in
    // questionCodes, then inserts the given answers — rows for codes not submitted are untouched, and a submitted
    // code with no matching insert row is cleared. Mirrors ISubmissionFeedbackAnswerRepository.ReplaceForQuestionsAsync.
    Task ReplaceForQuestionsAsync(SubmissionId submissionId, ActivityId activityId, IReadOnlyList<string> questionCodes, IReadOnlyList<SubmissionActivityAnswer> answers, CancellationToken cancellationToken = default);
}