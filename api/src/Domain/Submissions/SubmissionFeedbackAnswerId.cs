namespace Domain.Submissions;

public record SubmissionFeedbackAnswerId(Guid Value)
{
    public static SubmissionFeedbackAnswerId Empty() => new(Guid.Empty);
    public static SubmissionFeedbackAnswerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}