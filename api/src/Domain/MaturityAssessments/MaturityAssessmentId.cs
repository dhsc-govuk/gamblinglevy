namespace Domain.MaturityAssessments;

public record MaturityAssessmentId(Guid Value)
{
    public static MaturityAssessmentId Empty() => new(Guid.Empty);
    public static MaturityAssessmentId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}