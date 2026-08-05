namespace Domain.Funding;

public record FundingWindowId(Guid Value)
{
    public static FundingWindowId Empty() => new(Guid.Empty);
    public static FundingWindowId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}