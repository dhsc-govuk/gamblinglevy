using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Exceptions;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.Users;

/// <summary>
/// The caller and the user they named, for every command that acts on one existing user.
/// </summary>
/// <param name="OrganisationId">The user's organisation, or null for a DHSC user. Recorded on the audit row, which
/// is what puts it in that LA's own audit log, whichever route the action came through.</param>
public sealed record UserTarget(UserId ActorId, User User, OrganisationId? OrganisationId);

/// <summary>
/// Finds the user a lifecycle command names, within what the caller may see: DHSC admin reaches every user, an LA
/// role only its own LA's users, so another LA's user — or a DHSC one — is "does not exist", never "forbidden".
///
/// The DHSC routes (admin/users/{id}/…) and the LA routes (organisations/{organisationId}/users/{id}/…) send the
/// same commands. The scope comes from the caller's claims, never from the route's organisation id, which is why
/// the commands do not carry one.
/// </summary>
public class UserTargetResolver(
    PermissionsService permissionsService,
    IUserRepository userRepository,
    IOrganisationUserRepository organisationUserRepository)
{
    public async Task<Either<UserException, UserTarget>> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var targetId = new UserId(userId);

        return await permissionsService.GetOrganisationPermissions().MatchAsync(
            permission => permissionsService.GetUserId().MatchAsync<Either<UserException, UserTarget>>(
                actorId => FindAsync(targetId, actorId, permission, cancellationToken),
                e => new UserUnknownException(targetId, e)),
            e => new UserArgumentException(UserId.Empty(), e.Message));
    }

    private async Task<Either<UserException, UserTarget>> FindAsync(
        UserId targetId,
        UserId actorId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindInScope(targetId, permission, cancellationToken);
        if (user.IsNone)
        {
            return new UserDoesNotExistException(targetId);
        }

        var organisationId = await organisationUserRepository.FindOrganisationIdForUser(targetId, cancellationToken);

        return new UserTarget(actorId, user.ValueUnsafe(), organisationId.IsSome ? organisationId.ValueUnsafe() : null);
    }
}