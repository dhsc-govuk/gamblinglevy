namespace Domain.MaturityAssessments;

public record MaturityAssessmentAnswerId(Guid Value)
{
    public static MaturityAssessmentAnswerId Empty() => new(Guid.Empty);
    public static MaturityAssessmentAnswerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}