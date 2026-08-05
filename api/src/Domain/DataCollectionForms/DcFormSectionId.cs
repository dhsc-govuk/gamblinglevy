namespace Domain.DataCollectionForms;

public record DcFormSectionId(Guid Value)
{
    public static DcFormSectionId Empty() => new(Guid.Empty);
    public static DcFormSectionId New() => new(Guid.NewGuid());
    public static DcFormSectionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}