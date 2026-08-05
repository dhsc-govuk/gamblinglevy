using Domain.Common;

namespace Domain.Organisations;

public static class OrgTypes
{
    public const string LocalAuthority = "LA";
    public const string Vcse = "VCSE";
}

public class OrgType : ValueObject
{
    private string Value { get; }

    private OrgType(string value)
    {
        Value = value;
    }

    public static OrgType From(string value)
    {
        var type = new OrgType(value);

        if (!SupportedTypes.Contains(type))
        {
            throw new UnsupportedOrgTypeException(value);
        }

        return type;
    }

    public static OrgType LocalAuthority => new(OrgTypes.LocalAuthority);
    public static OrgType Vcse => new(OrgTypes.Vcse);

    // The values From() accepts. Exposed so validators can reject an unsupported value with a 400 rather
    // than letting From() throw out of a handler as an unhandled 500 (BLUM-7240).
    public static IEnumerable<string> SupportedTypeNames => SupportedTypes.Select(type => type.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedTypeNames.Contains(value, StringComparer.Ordinal);
    }

    public static implicit operator string(OrgType type) => type.ToString();
    public static explicit operator OrgType(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<OrgType> SupportedTypes
    {
        get
        {
            yield return LocalAuthority;
            yield return Vcse;
        }
    }
}

public class UnsupportedOrgTypeException(string value) : Exception($"OrgType \"{value}\" is not supported.");