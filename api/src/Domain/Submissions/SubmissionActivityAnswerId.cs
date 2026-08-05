namespace Domain.Submissions;

public record SubmissionActivityAnswerId(Guid Value)
{
    public static SubmissionActivityAnswerId Empty() => new(Guid.Empty);
    public static SubmissionActivityAnswerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}