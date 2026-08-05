namespace Domain.DataCollectionForms;

public record DcFormQuestionOptionId(Guid Value)
{
    public static DcFormQuestionOptionId Empty() => new(Guid.Empty);
    public static DcFormQuestionOptionId New() => new(Guid.NewGuid());
    public static DcFormQuestionOptionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}