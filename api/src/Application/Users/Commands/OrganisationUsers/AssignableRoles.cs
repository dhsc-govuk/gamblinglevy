using Domain.ValueObjects;

namespace Application.Users.Commands.OrganisationUsers;

internal static class AssignableRoles
{
    // Deliberately its own list rather than UserRole.IsOrganisationMember(): "is an LA role" and "an LA
    // admin may grant it" are different questions that only happen to share an answer for the BLUM-7038
    // role set. Widening the LA-facing [Authorize] lists must not silently widen what LA admins can mint.
    public static bool IsAssignableByOrganisationAdmin(UserRole role)
    {
        return role.Equals(UserRole.OrganisationAdmin)
               || role.Equals(UserRole.Lead);
    }

    public static bool IsAssignableBy(UserRole actorRole, UserRole targetRole)
    {
        return (actorRole.Equals(UserRole.Admin) || actorRole.Equals(UserRole.OrganisationAdmin))
               && IsAssignableByOrganisationAdmin(targetRole);
    }
}