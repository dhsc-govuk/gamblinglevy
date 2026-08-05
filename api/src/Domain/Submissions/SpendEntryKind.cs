using Domain.Common;

namespace Domain.Submissions;

public static class SpendEntryKinds
{
    public const string Actual = "actual";
    public const string Forecast = "forecast";
}

// What a SubmissionSpendEntry records: Actual = levy spent in the reporting quarter; Forecast = expected spend
// for a future quarter (carries the Quarter it forecasts). Mirrors the SubmissionSectionStatus ValueObject so EF
// converts it the same way (string column + From()).
public class SpendEntryKind : ValueObject
{
    private string Value { get; }

    private SpendEntryKind(string value)
    {
        Value = value;
    }

    public static SpendEntryKind From(string value)
    {
        var kind = new SpendEntryKind(value);

        if (!SupportedKinds.Contains(kind))
        {
            throw new UnsupportedSpendEntryKindException(value);
        }

        return kind;
    }

    public static SpendEntryKind Actual => new(SpendEntryKinds.Actual);
    public static SpendEntryKind Forecast => new(SpendEntryKinds.Forecast);

    public bool IsActual => Value == SpendEntryKinds.Actual;
    public bool IsForecast => Value == SpendEntryKinds.Forecast;

    public static implicit operator string(SpendEntryKind kind) => kind.ToString();
    public static explicit operator SpendEntryKind(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<SpendEntryKind> SupportedKinds
    {
        get
        {
            yield return Actual;
            yield return Forecast;
        }
    }
}

public class UnsupportedSpendEntryKindException(string value)
    : Exception($"SpendEntryKind \"{value}\" is not supported.");