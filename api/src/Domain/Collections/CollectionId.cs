namespace Domain.Collections;

public record CollectionId(Guid Value)
{
    public static CollectionId Empty() => new(Guid.Empty);
    public static CollectionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}