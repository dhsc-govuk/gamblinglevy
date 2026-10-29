namespace Domain.MaturityAssessments;

public record MaturityBaselineAnswerId(Guid Value)
{
    public static MaturityBaselineAnswerId Empty() => new(Guid.Empty);
    public static MaturityBaselineAnswerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}