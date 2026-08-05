namespace Domain.KeyResults;

public record KeyResultQuestionId(Guid Value)
{
    public static KeyResultQuestionId Empty() => new(Guid.Empty);
    public static KeyResultQuestionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}