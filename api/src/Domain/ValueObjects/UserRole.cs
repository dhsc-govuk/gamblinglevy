using Domain.Common;

namespace Domain.ValueObjects;

public static class Role
{
    public const string Admin = "admin";
    public const string OrganisationAdmin = "organisation admin";
    public const string Lead = "lead";

    // Every role scoped to a local authority rather than DHSC, and the [Authorize] role list for the
    // LA-facing controllers. UserRole.IsOrganisationMember() is derived from this single declaration:
    // Lead used to appear in the attribute lists but not in UserProfileRepository's hand-written role
    // list, so Leads authorised and then received no claims at all (BLUM-7038). Deriving the predicate
    // from the const is what stops the two drifting again.
    public const string AnyOrganisationMember = $"{OrganisationAdmin},{Lead}";

    // Roles permitted to create/edit/deactivate/delete users. Lead reads its own LA's user list but
    // cannot change it, so it is deliberately absent (BLUM-7038).
    public const string UserManagers = $"{Admin},{OrganisationAdmin}";
}

public class UserRole : ValueObject
{
    // Split of a const, so the string is baked at compile time and there is no static-init ordering
    // hazard. See Role.AnyOrganisationMember for why this is derived rather than hand-listed.
    private static readonly HashSet<string> OrganisationMemberRoles =
        Role.AnyOrganisationMember.Split(',').ToHashSet(StringComparer.Ordinal);

    private string Value { get; }

    private UserRole(string role)
    {
        Value = role;
    }

    public static UserRole From(string value)
    {
        var role = new UserRole(value);

        if (!SupportedRoles.Contains(role))
        {
            throw new UnsupportedUserRoleException(value);
        }

        return role;
    }

    public static UserRole Admin => new(Role.Admin);
    public static UserRole OrganisationAdmin => new(Role.OrganisationAdmin);
    public static UserRole Lead => new(Role.Lead);

    // The role strings From() accepts. Exposed so validators can reject an unsupported role with a 400
    // rather than letting From() throw out of a handler as an unhandled 500.
    public static IEnumerable<string> SupportedRoleNames => SupportedRoles.Select(role => role.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedRoleNames.Contains(value, StringComparer.Ordinal);
    }

    public static implicit operator string(UserRole role)
    {
        return role.ToString();
    }

    public static explicit operator UserRole(string value)
    {
        return From(value);
    }

    // True for roles belonging to a local authority, which therefore carry an organisationId claim and
    // resolve their profile through organisation_users.
    public bool IsOrganisationMember()
    {
        return OrganisationMemberRoles.Contains(Value);
    }

    public override string ToString()
    {
        return Value;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<UserRole> SupportedRoles
    {
        get
        {
            yield return Admin;
            yield return OrganisationAdmin;
            yield return Lead;
        }
    }
}

public class UnsupportedUserRoleException(string value) : Exception($"UserRole \"{value}\" is not supported.");