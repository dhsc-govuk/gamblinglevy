namespace Domain.Collections;

public record CollectionReminderDispatchId(Guid Value)
{
    public static CollectionReminderDispatchId Empty() => new(Guid.Empty);
    public static CollectionReminderDispatchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}