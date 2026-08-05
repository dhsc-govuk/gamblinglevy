namespace Domain.Submissions;

public record SubmissionId(Guid Value)
{
    public static SubmissionId Empty() => new(Guid.Empty);
    public static SubmissionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}