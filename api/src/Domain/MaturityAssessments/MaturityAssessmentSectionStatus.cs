using Domain.Common;

namespace Domain.MaturityAssessments;

// The same three strings the quarterly report's SubmissionSectionStatuses use, on purpose: the client task list
// maps them to the GOV.UK tags with one shared helper (progressTaskStatus), and the DHSC status roll-up reads the
// assessment-level "submitted" state from SubmittedAt rather than from any of these.
public static class MaturityAssessmentSectionStatuses
{
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
}

// Progress of one domain (task-list section) of a maturity assessment: Not started → In progress (any draft answer
// saved on one of its pages) → Completed (the section's "Check your answers" Continue). A later draft save drops a
// Completed section back to In progress, exactly as the quarterly sections do.
public class MaturityAssessmentSectionStatus : ValueObject
{
    private string Value { get; }

    private MaturityAssessmentSectionStatus(string value)
    {
        Value = value;
    }

    public static MaturityAssessmentSectionStatus From(string value)
    {
        var status = new MaturityAssessmentSectionStatus(value);

        if (!SupportedStatuses.Contains(status))
        {
            throw new UnsupportedMaturityAssessmentSectionStatusException(value);
        }

        return status;
    }

    public static MaturityAssessmentSectionStatus NotStarted => new(MaturityAssessmentSectionStatuses.NotStarted);
    public static MaturityAssessmentSectionStatus InProgress => new(MaturityAssessmentSectionStatuses.InProgress);
    public static MaturityAssessmentSectionStatus Completed => new(MaturityAssessmentSectionStatuses.Completed);

    public bool IsNotStarted => Value == MaturityAssessmentSectionStatuses.NotStarted;
    public bool IsCompleted => Value == MaturityAssessmentSectionStatuses.Completed;

    public static implicit operator string(MaturityAssessmentSectionStatus status) => status.ToString();
    public static explicit operator MaturityAssessmentSectionStatus(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<MaturityAssessmentSectionStatus> SupportedStatuses
    {
        get
        {
            yield return NotStarted;
            yield return InProgress;
            yield return Completed;
        }
    }
}

public class UnsupportedMaturityAssessmentSectionStatusException(string value)
    : Exception($"MaturityAssessmentSectionStatus \"{value}\" is not supported.");