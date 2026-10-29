using Domain.ValueObjects;

namespace Application.Common.Permissions;

/// <summary>
/// Thrown when a <see cref="Permission"/> cannot be turned into an organisation scope: its role is neither DHSC
/// admin nor an organisation member, or it is an organisation member with no organisation.
///
/// Neither can come out of <see cref="PermissionsService"/>, so this is a programming error — a role added to
/// <c>UserRole.SupportedRoles</c> without a scope, or a permission built by hand. It is thrown rather than
/// answered with an empty result because an empty list reads as "nothing to see", which is how a role with no
/// access stays unnoticed (BLUM-7038, BLUM-8751).
/// </summary>
public class PermissionScopeException(UserRole role, string reason)
    : InvalidOperationException($"Cannot scope a query for role \"{role}\": {reason}.")
{
    public static PermissionScopeException UnscopedRole(UserRole role) =>
        new(role, "it is neither the DHSC admin role nor an organisation member role");

    public static PermissionScopeException MissingOrganisation(UserRole role) =>
        new(role, "an organisation member permission must carry an organisation");
}