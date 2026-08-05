using Domain.Common;

namespace Domain.Organisations;

public static class Regions
{
    public const string EastMidlands = "East Midlands";
    public const string EastOfEngland = "East of England";
    public const string London = "London";
    public const string NorthEast = "North East";
    public const string NorthWest = "North West";
    public const string SouthEast = "South East";
    public const string SouthWest = "South West";
    public const string WestMidlands = "West Midlands";
    public const string YorkshireAndTheHumber = "Yorkshire and the Humber";
}

public class Region : ValueObject
{
    private string Value { get; }

    private Region(string value)
    {
        Value = value;
    }

    public static Region From(string value)
    {
        var region = new Region(value);

        if (!SupportedRegions.Contains(region))
        {
            throw new UnsupportedRegionException(value);
        }

        return region;
    }

    public static Region EastMidlands => new(Regions.EastMidlands);
    public static Region EastOfEngland => new(Regions.EastOfEngland);
    public static Region London => new(Regions.London);
    public static Region NorthEast => new(Regions.NorthEast);
    public static Region NorthWest => new(Regions.NorthWest);
    public static Region SouthEast => new(Regions.SouthEast);
    public static Region SouthWest => new(Regions.SouthWest);
    public static Region WestMidlands => new(Regions.WestMidlands);
    public static Region YorkshireAndTheHumber => new(Regions.YorkshireAndTheHumber);

    // The values From() accepts. Exposed so validators can reject an unsupported value with a 400 rather
    // than letting From() throw out of a handler as an unhandled 500. Materialised once rather than
    // re-enumerated per request, which is what SupportedRegions.Any(...) in the validators was doing
    // (BLUM-7240).
    public static IReadOnlySet<string> SupportedRegionNames { get; } =
        SupportedRegions.Select(region => region.Value).ToHashSet(StringComparer.Ordinal);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedRegionNames.Contains(value);
    }

    public static implicit operator string(Region region) => region.ToString();
    public static explicit operator Region(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public static IEnumerable<Region> SupportedRegions
    {
        get
        {
            yield return EastMidlands;
            yield return EastOfEngland;
            yield return London;
            yield return NorthEast;
            yield return NorthWest;
            yield return SouthEast;
            yield return SouthWest;
            yield return WestMidlands;
            yield return YorkshireAndTheHumber;
        }
    }
}

public class UnsupportedRegionException(string value) : Exception($"Region \"{value}\" is not supported.");