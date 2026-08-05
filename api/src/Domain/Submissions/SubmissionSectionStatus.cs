using Domain.Common;

namespace Domain.Submissions;

public static class SubmissionSectionStatuses
{
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
}

// Progress of one task-list section of a quarterly-report submission (and, reused, of one activity's report inside
// the "Enter activity key results" section): Not started → In progress (any draft answers saved) → Completed.
public class SubmissionSectionStatus : ValueObject
{
    private string Value { get; }

    private SubmissionSectionStatus(string value)
    {
        Value = value;
    }

    public static SubmissionSectionStatus From(string value)
    {
        var status = new SubmissionSectionStatus(value);

        if (!SupportedStatuses.Contains(status))
        {
            throw new UnsupportedSubmissionSectionStatusException(value);
        }

        return status;
    }

    public static SubmissionSectionStatus NotStarted => new(SubmissionSectionStatuses.NotStarted);
    public static SubmissionSectionStatus InProgress => new(SubmissionSectionStatuses.InProgress);
    public static SubmissionSectionStatus Completed => new(SubmissionSectionStatuses.Completed);

    public bool IsNotStarted => Value == SubmissionSectionStatuses.NotStarted;
    public bool IsCompleted => Value == SubmissionSectionStatuses.Completed;

    public static implicit operator string(SubmissionSectionStatus status) => status.ToString();
    public static explicit operator SubmissionSectionStatus(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<SubmissionSectionStatus> SupportedStatuses
    {
        get
        {
            yield return NotStarted;
            yield return InProgress;
            yield return Completed;
        }
    }
}

public class UnsupportedSubmissionSectionStatusException(string value)
    : Exception($"SubmissionSectionStatus \"{value}\" is not supported.");