using Domain.Common;

namespace Domain.Activities;

public static class ActivityStatuses
{
    public const string Planned = "planned";
    public const string Active = "active";
    public const string Closed = "closed";
    public const string Finished = "finished";

    // Not an ActivityStatus, and deliberately not in ActivityStatus.SupportedStatusNames: a draft is a record
    // whose setup is unfinished (IsComplete == false) and which may therefore have no status at all. It is a
    // legal value for the list endpoint's status filter, which the repository maps onto IsComplete rather than
    // onto the status column, so the filter's allow-list is the supported names plus this one.
    public const string DraftFilter = "draft";
}

// The user-chosen lifecycle status of a completed activity record (BLUM redesign). Status is independent of
// whether the record's setup is finished: an incomplete record is a "Draft" (Activity.IsComplete == false)
// and may have no status yet. Closed/Finished records are read-only.
public class ActivityStatus : ValueObject
{
    private string Value { get; }

    private ActivityStatus(string value)
    {
        Value = value;
    }

    public static ActivityStatus From(string value)
    {
        var status = new ActivityStatus(value);

        if (!SupportedStatuses.Contains(status))
        {
            throw new UnsupportedActivityStatusException(value);
        }

        return status;
    }

    public static ActivityStatus Planned => new(ActivityStatuses.Planned);
    public static ActivityStatus Active => new(ActivityStatuses.Active);
    public static ActivityStatus Closed => new(ActivityStatuses.Closed);
    public static ActivityStatus Finished => new(ActivityStatuses.Finished);

    // The values From() accepts. Exposed so validators and query-string guards can reject an unsupported
    // value with a 400 rather than letting From() throw out of a handler as an unhandled 500, and so the
    // allow-list is declared once instead of being restated in each validator (BLUM-7240).
    public static IEnumerable<string> SupportedStatusNames => SupportedStatuses.Select(status => status.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedStatusNames.Contains(value, StringComparer.Ordinal);
    }

    public bool IsActive => Value == ActivityStatuses.Active;

    // Only a Closed record carries a closure reason (BLUM-7673).
    public bool IsClosed => Value == ActivityStatuses.Closed;

    // "Read-only" records (closed or finished) cannot be edited once setup is complete; the UI shows them
    // without "Change" links.
    public bool IsReadOnly => Value is ActivityStatuses.Closed or ActivityStatuses.Finished;

    public static implicit operator string(ActivityStatus status) => status.ToString();
    public static explicit operator ActivityStatus(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<ActivityStatus> SupportedStatuses
    {
        get
        {
            yield return Planned;
            yield return Active;
            yield return Closed;
            yield return Finished;
        }
    }
}

public class UnsupportedActivityStatusException(string value) : Exception($"ActivityStatus \"{value}\" is not supported.");