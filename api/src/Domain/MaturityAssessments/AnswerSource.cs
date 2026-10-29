using Domain.Common;

namespace Domain.MaturityAssessments;

// Where a maturity-assessment answer came from (BLUM-7856 AC2: "the source is recorded"). Pre-fill writes the
// first three at Start, and Quarterly again when a later quarterly report is submitted (BLUM-8927); every save by
// the LA stamps User, so "still pre-filled" and "reviewed and kept" are distinguishable long after the fact.
public static class AnswerSources
{
    // Derived from the organisation's latest SUBMITTED quarterly report(s) via a green row of the MA/QP mapping.
    public const string Quarterly = "quarterly";

    // Copied from the organisation's most recent submitted maturity assessment for an earlier financial year.
    public const string PriorAssessment = "prior_assessment";

    // Loaded from the 2025 Microsoft Forms survey by the baseline seeder (BLUM-7857).
    public const string Baseline2025 = "baseline_2025";

    // Typed (or confirmed by re-saving) by a user of the organisation.
    public const string User = "user";
}

public class AnswerSource : ValueObject
{
    private string Value { get; }

    private AnswerSource(string value)
    {
        Value = value;
    }

    public static AnswerSource From(string value)
    {
        var source = new AnswerSource(value);

        if (!SupportedSources.Contains(source))
        {
            throw new UnsupportedAnswerSourceException(value);
        }

        return source;
    }

    public static AnswerSource Quarterly => new(AnswerSources.Quarterly);
    public static AnswerSource PriorAssessment => new(AnswerSources.PriorAssessment);
    public static AnswerSource Baseline2025 => new(AnswerSources.Baseline2025);
    public static AnswerSource User => new(AnswerSources.User);

    public bool IsUser => Value == AnswerSources.User;

    public static IEnumerable<string> SupportedNames => SupportedSources.Select(source => source.Value);

    public static implicit operator string(AnswerSource source) => source.ToString();
    public static explicit operator AnswerSource(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<AnswerSource> SupportedSources
    {
        get
        {
            yield return Quarterly;
            yield return PriorAssessment;
            yield return Baseline2025;
            yield return User;
        }
    }
}

public class UnsupportedAnswerSourceException(string value)
    : Exception($"AnswerSource \"{value}\" is not supported.");