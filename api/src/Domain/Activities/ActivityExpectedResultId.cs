namespace Domain.Activities;

public record ActivityExpectedResultId(Guid Value)
{
    public static ActivityExpectedResultId Empty() => new(Guid.Empty);
    public static ActivityExpectedResultId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}