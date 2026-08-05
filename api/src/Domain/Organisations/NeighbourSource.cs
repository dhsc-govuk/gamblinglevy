using Domain.Common;

namespace Domain.Organisations;

public static class NeighbourSources
{
    public const string Statistical = "statistical";
    public const string Geographic = "geographic";
}

public class NeighbourSource : ValueObject
{
    private string Value { get; }

    private NeighbourSource(string value)
    {
        Value = value;
    }

    public static NeighbourSource From(string value)
    {
        var source = new NeighbourSource(value);

        if (!SupportedSources.Contains(source))
        {
            throw new UnsupportedNeighbourSourceException(value);
        }

        return source;
    }

    public static NeighbourSource Statistical => new(NeighbourSources.Statistical);
    public static NeighbourSource Geographic => new(NeighbourSources.Geographic);

    public static implicit operator string(NeighbourSource source) => source.ToString();
    public static explicit operator NeighbourSource(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<NeighbourSource> SupportedSources
    {
        get
        {
            yield return Statistical;
            yield return Geographic;
        }
    }
}

public class UnsupportedNeighbourSourceException(string value) : Exception($"NeighbourSource \"{value}\" is not supported.");