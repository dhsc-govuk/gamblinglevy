namespace Domain.LocationUsers;

public record LocationUserId(Guid Value)
{
    public static LocationUserId Empty() => new(Guid.Empty);
    public static LocationUserId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}