using Domain.Common;

namespace Domain.ValueObjects;

public static class Status
{
    public const string Active = "active";
    public const string Deactivated = "deactivated";
}

public class UserStatus : ValueObject
{
    private string Value { get; }

    private UserStatus(string status)
    {
        Value = status;
    }

    public static UserStatus From(string value)
    {
        var status = new UserStatus(value);

        if (!SupportedStatuses.Contains(status))
        {
            throw new UnsupportedUserStatusException(value);
        }

        return status;
    }

    public static UserStatus Active => new(Status.Active);
    public static UserStatus Deactivated => new(Status.Deactivated);

    public static implicit operator string(UserStatus status)
    {
        return status.ToString();
    }

    public static explicit operator UserStatus(string value)
    {
        return From(value);
    }

    public override string ToString()
    {
        return Value;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<UserStatus> SupportedStatuses
    {
        get
        {
            yield return Active;
            yield return Deactivated;
        }
    }
}

public class UnsupportedUserStatusException(string value) : Exception($"UserStatus \"{value}\" is not supported.");