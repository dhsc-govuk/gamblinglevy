namespace Domain.Forms;

public record FormQuestionOptionId(Guid Value)
{
    public static FormQuestionOptionId Empty() => new(Guid.Empty);
    public static FormQuestionOptionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}