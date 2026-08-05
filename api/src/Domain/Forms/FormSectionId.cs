namespace Domain.Forms;

public record FormSectionId(Guid Value)
{
    public static FormSectionId Empty() => new(Guid.Empty);
    public static FormSectionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}