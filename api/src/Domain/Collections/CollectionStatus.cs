using Domain.Common;

namespace Domain.Collections;

public static class CollectionStatuses
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Open = "open";
    public const string Closed = "closed";
}

// The derived lifecycle status of a collection. This is not stored: Collection.GetStatus() computes it from the
// draft/manually-closed flags and the open/deadline dates relative to today.
public class CollectionStatus : ValueObject
{
    private string Value { get; }

    private CollectionStatus(string value)
    {
        Value = value;
    }

    public static CollectionStatus From(string value)
    {
        var status = new CollectionStatus(value);

        if (!SupportedStatuses.Contains(status))
        {
            throw new UnsupportedCollectionStatusException(value);
        }

        return status;
    }

    public static CollectionStatus Draft => new(CollectionStatuses.Draft);
    public static CollectionStatus Scheduled => new(CollectionStatuses.Scheduled);
    public static CollectionStatus Open => new(CollectionStatuses.Open);
    public static CollectionStatus Closed => new(CollectionStatuses.Closed);

    public static implicit operator string(CollectionStatus status) => status.ToString();
    public static explicit operator CollectionStatus(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<CollectionStatus> SupportedStatuses
    {
        get
        {
            yield return Draft;
            yield return Scheduled;
            yield return Open;
            yield return Closed;
        }
    }
}

public class UnsupportedCollectionStatusException(string value) : Exception($"CollectionStatus \"{value}\" is not supported.");