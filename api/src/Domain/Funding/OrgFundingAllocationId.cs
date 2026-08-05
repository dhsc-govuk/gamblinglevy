namespace Domain.Funding;

public record OrgFundingAllocationId(Guid Value)
{
    public static OrgFundingAllocationId Empty() => new(Guid.Empty);
    public static OrgFundingAllocationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}