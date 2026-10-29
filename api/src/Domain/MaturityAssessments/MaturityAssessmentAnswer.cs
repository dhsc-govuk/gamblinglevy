using Domain.Common;
using Domain.Users;

namespace Domain.MaturityAssessments;

// A single answer to one maturity-assessment question inside one assessment. QuestionCode and Value are snapshotted
// strings keyed on the static protocol's stable codes; multi-select values are comma-joined option values and the
// funded-roles repeater stores one JSON array. Source records where the value came from (pre-fill or the user).
// Replaced per question code on every page save (the quarterly per-page merge contract) — never updated in place.
public class MaturityAssessmentAnswer : IEntity<MaturityAssessmentAnswerId>
{
    public MaturityAssessmentAnswerId Id { get; private set; }
    public MaturityAssessmentId AssessmentId { get; private set; }
    public string QuestionCode { get; private set; }
    public string Value { get; private set; }
    public AnswerSource Source { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private MaturityAssessmentAnswer(
        MaturityAssessmentAnswerId id,
        MaturityAssessmentId assessmentId,
        string questionCode,
        string value,
        AnswerSource source,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        AssessmentId = assessmentId;
        QuestionCode = questionCode;
        Value = value;
        Source = source;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public static MaturityAssessmentAnswer Record(
        MaturityAssessmentAnswerId id,
        MaturityAssessmentId assessmentId,
        string questionCode,
        string value,
        AnswerSource source,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(assessmentId, nameof(assessmentId));
        Guard.NotNullOrEmptyOrWhiteSpace(questionCode, nameof(questionCode));
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new MaturityAssessmentAnswer(
            id,
            assessmentId,
            questionCode.Trim(),
            value ?? string.Empty,
            source,
            createdBy,
            utcNow);
    }
}