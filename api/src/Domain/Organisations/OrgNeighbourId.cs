namespace Domain.Organisations;

public record OrgNeighbourId(Guid Value)
{
    public static OrgNeighbourId Empty() => new(Guid.Empty);
    public static OrgNeighbourId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}