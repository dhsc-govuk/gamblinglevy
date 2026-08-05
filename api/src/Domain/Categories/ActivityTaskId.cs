namespace Domain.Categories;

public record ActivityTaskId(Guid Value)
{
    public static ActivityTaskId Empty() => new(Guid.Empty);
    public static ActivityTaskId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}