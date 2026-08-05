using Domain.Common;

namespace Domain.Organisations;

public static class OnboardingStatuses
{
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Suspended = "suspended";
}

public class OnboardingStatus : ValueObject
{
    private string Value { get; }

    private OnboardingStatus(string value)
    {
        Value = value;
    }

    public static OnboardingStatus From(string value)
    {
        var status = new OnboardingStatus(value);

        if (!SupportedStatuses.Contains(status))
        {
            throw new UnsupportedOnboardingStatusException(value);
        }

        return status;
    }

    public static OnboardingStatus Invited => new(OnboardingStatuses.Invited);
    public static OnboardingStatus Active => new(OnboardingStatuses.Active);
    public static OnboardingStatus Suspended => new(OnboardingStatuses.Suspended);

    // The values From() accepts. Exposed so validators and query-string guards can reject an unsupported
    // value with a 400 — until BLUM-7240, ?onboardingStatus=bogus reached From() inside a repository Where
    // clause and surfaced as an unhandled 500.
    public static IEnumerable<string> SupportedStatusNames => SupportedStatuses.Select(status => status.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedStatusNames.Contains(value, StringComparer.Ordinal);
    }

    public static implicit operator string(OnboardingStatus status) => status.ToString();
    public static explicit operator OnboardingStatus(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<OnboardingStatus> SupportedStatuses
    {
        get
        {
            yield return Invited;
            yield return Active;
            yield return Suspended;
        }
    }
}

public class UnsupportedOnboardingStatusException(string value) : Exception($"OnboardingStatus \"{value}\" is not supported.");