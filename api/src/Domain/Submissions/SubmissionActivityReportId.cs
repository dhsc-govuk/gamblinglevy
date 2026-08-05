namespace Domain.Submissions;

public record SubmissionActivityReportId(Guid Value)
{
    public static SubmissionActivityReportId Empty() => new(Guid.Empty);
    public static SubmissionActivityReportId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}