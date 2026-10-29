namespace Domain.DataCollectionForms;

public record FormModuleVersionId(Guid Value)
{
    public static FormModuleVersionId Empty() => new(Guid.Empty);
    public static FormModuleVersionId New() => new(Guid.NewGuid());
    public static FormModuleVersionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}