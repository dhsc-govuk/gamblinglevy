using Domain.Common;

namespace Domain.Collections;

public static class ReminderTriggers
{
    // A configured CollectionReminder whose fire date has been reached (BLUM-7571).
    public const string Scheduled = "scheduled";

    // An admin pressing "Send manual reminder", outside the configured cadence.
    public const string Manual = "manual";

    // The collection's start date being reached (BLUM-7721). Not a configured CollectionReminder: the window
    // opening is owed for every published collection, whether or not an admin set a cadence up.
    public const string WindowOpened = "window_opened";
}

// What caused a CollectionReminderDispatch. Modelled the same way as ReminderOffsetType — a ValueObject with a
// throwing From() and an IsSupported() escape hatch — so a value that reaches the domain from outside is rejected
// at the boundary rather than 500ing out of a handler (BLUM-7240).
//
// It is part of the dispatch table's unique key, so a later trigger kind cannot collide with a scheduled reminder
// that happens to fall on the same day — which is how "window_opened" (BLUM-7721) coexists with a configured
// after_open day-0 reminder resolving to the same date. BLUM-7572 will add "escalation" the same way.
public class ReminderTrigger : ValueObject
{
    private string Value { get; }

    private ReminderTrigger(string value)
    {
        Value = value;
    }

    public static ReminderTrigger From(string value)
    {
        var trigger = new ReminderTrigger(value);

        if (!SupportedTriggers.Contains(trigger))
        {
            throw new UnsupportedReminderTriggerException(value);
        }

        return trigger;
    }

    public static ReminderTrigger Scheduled => new(ReminderTriggers.Scheduled);
    public static ReminderTrigger Manual => new(ReminderTriggers.Manual);
    public static ReminderTrigger WindowOpened => new(ReminderTriggers.WindowOpened);

    public static IEnumerable<string> SupportedTriggerNames => SupportedTriggers.Select(trigger => trigger.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedTriggerNames.Contains(value, StringComparer.Ordinal);
    }

    public static implicit operator string(ReminderTrigger trigger) => trigger.ToString();
    public static explicit operator ReminderTrigger(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<ReminderTrigger> SupportedTriggers
    {
        get
        {
            yield return Scheduled;
            yield return Manual;
            yield return WindowOpened;
        }
    }
}

public class UnsupportedReminderTriggerException(string value) : Exception($"ReminderTrigger \"{value}\" is not supported.");