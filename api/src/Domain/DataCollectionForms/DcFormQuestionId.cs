namespace Domain.DataCollectionForms;

public record DcFormQuestionId(Guid Value)
{
    public static DcFormQuestionId Empty() => new(Guid.Empty);
    public static DcFormQuestionId New() => new(Guid.NewGuid());
    public static DcFormQuestionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}