using Domain.Common;
using Domain.Forms;
using Domain.Users;

namespace Domain.Activities;

// A single answer submitted against a dynamic activity-record question (BLUM-6444). Modelled as a separate
// aggregate with an id-only ActivityId reference so it persists through its own repository without EF tracking
// conflicts. QuestionCode and Value are snapshotted, so historic answers remain readable even if the question is
// later renamed, deactivated or deleted.
public class ActivityAnswer : IEntity<ActivityAnswerId>
{
    public ActivityAnswerId Id { get; private set; }
    public ActivityId ActivityId { get; private set; }
    public FormQuestionId QuestionId { get; private set; }
    public string QuestionCode { get; private set; }
    public string Value { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ActivityAnswer(
        ActivityAnswerId id,
        ActivityId activityId,
        FormQuestionId questionId,
        string questionCode,
        string value,
        UserId createdBy,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        ActivityId = activityId;
        QuestionId = questionId;
        QuestionCode = questionCode;
        Value = value;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static ActivityAnswer Record(
        ActivityAnswerId id,
        ActivityId activityId,
        FormQuestionId questionId,
        string questionCode,
        string value,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(activityId, nameof(activityId));
        Guard.NotNull(questionId, nameof(questionId));
        Guard.NotNullOrEmptyOrWhiteSpace(questionCode, nameof(questionCode));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new ActivityAnswer(
            id,
            activityId,
            questionId,
            questionCode.Trim(),
            value ?? string.Empty,
            createdBy,
            utcNow,
            null);
    }
}