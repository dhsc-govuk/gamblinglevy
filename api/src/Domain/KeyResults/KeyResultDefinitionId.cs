namespace Domain.KeyResults;

public record KeyResultDefinitionId(Guid Value)
{
    public static KeyResultDefinitionId Empty() => new(Guid.Empty);
    public static KeyResultDefinitionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}