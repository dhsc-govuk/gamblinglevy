using Domain.Common;

namespace Domain.Collections;

public static class ReminderOffsetTypes
{
    public const string AfterOpen = "after_open";
    public const string BeforeDeadline = "before_deadline";
}

public class ReminderOffsetType : ValueObject
{
    private string Value { get; }

    private ReminderOffsetType(string value)
    {
        Value = value;
    }

    public static ReminderOffsetType From(string value)
    {
        var offsetType = new ReminderOffsetType(value);

        if (!SupportedOffsetTypes.Contains(offsetType))
        {
            throw new UnsupportedReminderOffsetTypeException(value);
        }

        return offsetType;
    }

    public static ReminderOffsetType AfterOpen => new(ReminderOffsetTypes.AfterOpen);
    public static ReminderOffsetType BeforeDeadline => new(ReminderOffsetTypes.BeforeDeadline);

    // The values From() accepts. Exposed so validators can reject an unsupported value with a 400 rather
    // than letting From() throw out of a handler as an unhandled 500 (BLUM-7240).
    public static IEnumerable<string> SupportedOffsetTypeNames => SupportedOffsetTypes.Select(offsetType => offsetType.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedOffsetTypeNames.Contains(value, StringComparer.Ordinal);
    }

    public static implicit operator string(ReminderOffsetType offsetType) => offsetType.ToString();
    public static explicit operator ReminderOffsetType(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<ReminderOffsetType> SupportedOffsetTypes
    {
        get
        {
            yield return AfterOpen;
            yield return BeforeDeadline;
        }
    }
}

public class UnsupportedReminderOffsetTypeException(string value) : Exception($"ReminderOffsetType \"{value}\" is not supported.");