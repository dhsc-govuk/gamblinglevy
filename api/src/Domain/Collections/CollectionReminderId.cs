namespace Domain.Collections;

public record CollectionReminderId(Guid Value)
{
    public static CollectionReminderId Empty() => new(Guid.Empty);
    public static CollectionReminderId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}