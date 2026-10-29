using Domain.Common;

namespace Domain.Collections;

public static class CollectionTypes
{
    public const string QuarterlyReport = "quarterly_report";
    public const string MaturityAssessment = "maturity_assessment";
}

public class CollectionType : ValueObject
{
    private string Value { get; }

    private CollectionType(string value)
    {
        Value = value;
    }

    public static CollectionType From(string value)
    {
        var type = new CollectionType(value);

        if (!SupportedTypes.Contains(type))
        {
            throw new UnsupportedCollectionTypeException(value);
        }

        return type;
    }

    public static CollectionType QuarterlyReport => new(CollectionTypes.QuarterlyReport);
    public static CollectionType MaturityAssessment => new(CollectionTypes.MaturityAssessment);

    // The values From() accepts. Exposed so validators can reject an unsupported value with a 400 rather
    // than letting From() throw out of a handler as an unhandled 500 (BLUM-7240).
    public static IEnumerable<string> SupportedTypeNames => SupportedTypes.Select(type => type.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedTypeNames.Contains(value, StringComparer.Ordinal);
    }

    public bool IsQuarterly => Value == CollectionTypes.QuarterlyReport;

    // The annual maturity assessment (BLUM-7855): a full-financial-year window with no quarter, answered once a
    // year through the MaturityAssessments aggregate rather than the quarterly Submission flow.
    public bool IsMaturityAssessment => Value == CollectionTypes.MaturityAssessment;

    public static implicit operator string(CollectionType type) => type.ToString();
    public static explicit operator CollectionType(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<CollectionType> SupportedTypes
    {
        get
        {
            yield return QuarterlyReport;
            yield return MaturityAssessment;
        }
    }
}

public class UnsupportedCollectionTypeException(string value) : Exception($"CollectionType \"{value}\" is not supported.");