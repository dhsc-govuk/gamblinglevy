using Domain.Common;
using Domain.Users;

namespace Domain.Submissions;

// A single answer to a "Provide strategic feedback" question inside one submission. Unlike SubmissionActivityAnswer
// these are per-submission (no activity) — the feedback questions are a fixed, section-wide set. QuestionCode and
// Value are snapshotted strings (stable codes survive question rewording/deactivation). Replaced wholesale on every
// save (the save contract sends the full answer set).
public class SubmissionFeedbackAnswer : IEntity<SubmissionFeedbackAnswerId>
{
    public SubmissionFeedbackAnswerId Id { get; private set; }
    public SubmissionId SubmissionId { get; private set; }
    public string QuestionCode { get; private set; }
    public string Value { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private SubmissionFeedbackAnswer(
        SubmissionFeedbackAnswerId id,
        SubmissionId submissionId,
        string questionCode,
        string value,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        SubmissionId = submissionId;
        QuestionCode = questionCode;
        Value = value;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public static SubmissionFeedbackAnswer Record(
        SubmissionFeedbackAnswerId id,
        SubmissionId submissionId,
        string questionCode,
        string value,
        UserId createdBy)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(submissionId, nameof(submissionId));
        Guard.NotNullOrEmptyOrWhiteSpace(questionCode, nameof(questionCode));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new SubmissionFeedbackAnswer(
            id,
            submissionId,
            questionCode.Trim(),
            value ?? string.Empty,
            createdBy,
            DateTime.UtcNow);
    }
}