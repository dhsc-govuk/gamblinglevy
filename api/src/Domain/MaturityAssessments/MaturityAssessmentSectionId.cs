namespace Domain.MaturityAssessments;

public record MaturityAssessmentSectionId(Guid Value)
{
    public static MaturityAssessmentSectionId Empty() => new(Guid.Empty);
    public static MaturityAssessmentSectionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}