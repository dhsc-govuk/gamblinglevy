namespace Domain.Submissions;

public record SubmissionSectionId(Guid Value)
{
    public static SubmissionSectionId Empty() => new(Guid.Empty);
    public static SubmissionSectionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}