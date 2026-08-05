using Domain.Common;

namespace Domain.DataCollectionForms;

public static class FormStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

// The lifecycle status of a forms-builder module. A module is authored as a Draft, locked once Published
// (structure can no longer be edited), and may be Archived to retire it.
public class FormStatus : ValueObject
{
    private string Value { get; }

    private FormStatus(string value)
    {
        Value = value;
    }

    public static FormStatus From(string value)
    {
        var status = new FormStatus(value);

        if (!SupportedStatuses.Contains(status))
        {
            throw new UnsupportedFormStatusException(value);
        }

        return status;
    }

    public static FormStatus Draft => new(FormStatuses.Draft);
    public static FormStatus Published => new(FormStatuses.Published);
    public static FormStatus Archived => new(FormStatuses.Archived);

    public bool IsPublished => Value == FormStatuses.Published;

    public static implicit operator string(FormStatus status) => status.ToString();
    public static explicit operator FormStatus(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<FormStatus> SupportedStatuses
    {
        get
        {
            yield return Draft;
            yield return Published;
            yield return Archived;
        }
    }
}

public class UnsupportedFormStatusException(string value) : Exception($"FormStatus \"{value}\" is not supported.");