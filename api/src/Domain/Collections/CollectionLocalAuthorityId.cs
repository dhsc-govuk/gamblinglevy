namespace Domain.Collections;

public record CollectionLocalAuthorityId(Guid Value)
{
    public static CollectionLocalAuthorityId Empty() => new(Guid.Empty);
    public static CollectionLocalAuthorityId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}