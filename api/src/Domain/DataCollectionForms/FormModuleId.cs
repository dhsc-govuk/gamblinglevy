namespace Domain.DataCollectionForms;

public record FormModuleId(Guid Value)
{
    public static FormModuleId Empty() => new(Guid.Empty);
    public static FormModuleId New() => new(Guid.NewGuid());
    public static FormModuleId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}