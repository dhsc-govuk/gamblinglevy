using Domain.Activities;
using Domain.Common;
using Domain.Users;

namespace Domain.Submissions;

// A single answer to a quarterly-report question for one activity inside one submission. QuestionCode and Value are
// snapshotted strings (the ActivityAnswer precedent): codes are stable across the protocol's Active vs
// Closed/Finished phrasing variants, so answers survive an activity status change, question rewording or
// deactivation. Replaced wholesale per activity on every save.
public class SubmissionActivityAnswer : IEntity<SubmissionActivityAnswerId>
{
    public SubmissionActivityAnswerId Id { get; private set; }
    public SubmissionId SubmissionId { get; private set; }
    public ActivityId ActivityId { get; private set; }
    public string QuestionCode { get; private set; }
    public string Value { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private SubmissionActivityAnswer(
        SubmissionActivityAnswerId id,
        SubmissionId submissionId,
        ActivityId activityId,
        string questionCode,
        string value,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        SubmissionId = submissionId;
        ActivityId = activityId;
        QuestionCode = questionCode;
        Value = value;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public static SubmissionActivityAnswer Record(
        SubmissionActivityAnswerId id,
        SubmissionId submissionId,
        ActivityId activityId,
        string questionCode,
        string value,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(submissionId, nameof(submissionId));
        Guard.NotNull(activityId, nameof(activityId));
        Guard.NotNullOrEmptyOrWhiteSpace(questionCode, nameof(questionCode));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new SubmissionActivityAnswer(
            id,
            submissionId,
            activityId,
            questionCode.Trim(),
            value ?? string.Empty,
            createdBy,
            utcNow);
    }
}