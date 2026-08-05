namespace Domain.Forms;

public record FormQuestionId(Guid Value)
{
    public static FormQuestionId Empty() => new(Guid.Empty);
    public static FormQuestionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}