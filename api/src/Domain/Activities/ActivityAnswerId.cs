namespace Domain.Activities;

public record ActivityAnswerId(Guid Value)
{
    public static ActivityAnswerId Empty() => new(Guid.Empty);
    public static ActivityAnswerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}