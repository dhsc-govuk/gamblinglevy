using Application.Common.Interfaces.Repositories;
using Application.Users.Exceptions;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;

namespace Application.Users;

/// <summary>
/// Refuses a change that would leave nobody able to manage users: the last active DHSC admin, or the last active
/// LA Admin of an organisation, being deactivated, deleted, demoted or moved to another organisation.
///
/// The rule follows the target's role, not the route that asked. Before BLUM-8751 each of six handlers carried its
/// own copy, and the copies disagreed: DHSC could remove an LA's last LA Admin on its own routes but not on the LA
/// route, and the LA edit form could deactivate one outright. Whether a change counts as a removal is the caller's
/// to decide — only it knows whether the role or organisation is changing.
///
/// Organisation suspension deliberately does not call this: deactivating an organisation's last admin is exactly
/// what that cascade is for (see <see cref="UserDeactivator"/>).
/// </summary>
public class LastActiveAdminGuard(
    IUserRepository userRepository,
    IOrganisationUserRepository organisationUserRepository)
{
    /// <param name="user">The user as they are now, before the change.</param>
    /// <param name="organisationId">The organisation the user belongs to now; null for a DHSC user.</param>
    /// <returns>The refusal, or null when the change may go ahead.</returns>
    public async Task<UserException?> RefuseRemovalAsync(
        User user,
        OrganisationId? organisationId,
        CancellationToken cancellationToken)
    {
        if (!user.Status.Equals(UserStatus.Active))
        {
            return null;
        }

        if (user.Role.Equals(UserRole.Admin))
        {
            var admins = await userRepository.GetActiveAdminsAsync(cancellationToken);
            return IsOnlyOne(admins, user) ? new CannotRemoveLastActiveAdminException(user.Id, organisationId: null) : null;
        }

        if (user.Role.Equals(UserRole.OrganisationAdmin) && organisationId is not null)
        {
            var admins = await organisationUserRepository.GetActiveOrgAdminsAsync(organisationId, cancellationToken);
            return IsOnlyOne(admins, user) ? new CannotRemoveLastActiveAdminException(user.Id, organisationId) : null;
        }

        return null;
    }

    private static bool IsOnlyOne(IReadOnlyList<User> admins, User user) =>
        admins.Count == 1 && admins[0].Id == user.Id;
}