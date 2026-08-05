namespace Domain.Activities;

public record ActivityId(Guid Value)
{
    public static ActivityId Empty() => new(Guid.Empty);
    public static ActivityId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}