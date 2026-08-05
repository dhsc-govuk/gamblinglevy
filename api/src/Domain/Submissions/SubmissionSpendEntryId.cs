namespace Domain.Submissions;

public record SubmissionSpendEntryId(Guid Value)
{
    public static SubmissionSpendEntryId Empty() => new(Guid.Empty);
    public static SubmissionSpendEntryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}