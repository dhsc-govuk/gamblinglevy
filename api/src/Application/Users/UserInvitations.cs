using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Application.Users;

/// <summary>
/// Decides when a user's invitation email goes out — the sequence behind every way a user is created or reactivated.
///
/// A user created active is invited at once, as before. Since BLUM-7945 a user created deactivated is not: the
/// invitation is held (<see cref="User.InvitationHeldAt"/>) and sent the first time they are reactivated, because
/// an invitation to an account that cannot sign in only lands them on the "account deactivated" page. That is how
/// the contacts of an organisation created with "Leave it deactivated" hear about the service once DHSC reactivates
/// them. There are two creation paths and two reactivation paths (ReactivateUserCommand, which the DHSC and LA
/// routes both send, and the LA edit form switching a user back to active), which is why this is shared rather
/// than repeated.
///
/// "Can sign in" needs the user's organisation to be active as well as the user, so a held invitation is released
/// only once both are. Reactivating a user while their organisation is still deactivated keeps the hold, and
/// reactivating the organisation then sends it (<see cref="SendHeldOnOrganisationReactivationAsync"/>) — to users
/// already reactivated individually, never to anyone still deactivated: reactivating an organisation reactivates
/// none of its users (BLUM-7667 AC4).
///
/// Sending stays best-effort, as it always was on creation: a failed send is logged and never fails or rolls back
/// the creation or reactivation it belongs to. A held invitation is only released once it has actually been sent.
/// </summary>
public class UserInvitations(
    IInvitationEmailService invitationEmailService,
    IOrganisationUserRepository organisationUserRepository,
    IOrganisationRepository organisationRepository,
    IUserRepository userRepository,
    IDateTimeProvider dateTimeProvider,
    ILogger<UserInvitations> logger)
{
    /// <summary>
    /// Call on a new user before it is first saved, so the hold is persisted with it. Holds the invitation when the
    /// user cannot sign in yet: they are deactivated, or their organisation is (an admin may add an active user to
    /// a deactivated organisation — that is allowed, and they are invited when it is reactivated).
    /// </summary>
    /// <param name="organisation">The organisation the user is being added to; null for a DHSC user.</param>
    public void HoldIfCannotSignIn(User user, Organisation? organisation)
    {
        if (user.Status.Equals(UserStatus.Deactivated) || IsDeactivated(organisation))
        {
            user.HoldInvitation(dateTimeProvider.GetUtcNow());
        }
    }

    /// <summary>Call once the new user is saved. Sends the invitation unless <see cref="HoldIfCannotSignIn"/> held it.</summary>
    public async Task SendUnlessHeldAsync(User user, CancellationToken cancellationToken)
    {
        if (user.InvitationHeldAt is null)
        {
            var result = await invitationEmailService.SendInvitationAsync(user.Email, user.Name, user.Role, cancellationToken);
            result.IfLeft(e => logger.LogWarning(e, "Invitation email failed for {Email}; user creation not rolled back", user.Email));
        }
    }

    /// <summary>
    /// Whether this user is owed their held invitation now: they hold one, they are active, and their organisation
    /// (if they have one) is not deactivated. Changes nothing — the hold is cleared only by
    /// <see cref="SendHeldAsync"/>, once the email has actually gone.
    /// </summary>
    public async Task<bool> IsOwedAsync(User user, CancellationToken cancellationToken)
    {
        return user.InvitationHeldAt is not null
            && user.Status.Equals(UserStatus.Active)
            && !await IsInADeactivatedOrganisationAsync(user, cancellationToken);
    }

    /// <summary>
    /// Sends a held invitation and clears the hold, only if the send succeeded. Call once the user's reactivation
    /// is saved, and before anything else that could still fail — the audit write — so a failure there cannot
    /// cost the user their invitation.
    /// </summary>
    /// <remarks>
    /// Takes no CancellationToken, deliberately, like UserDeactivator: the reactivation is already committed, so an
    /// admin closing the tab must not be able to cancel the send that goes with it, which no retry would repeat.
    /// A failed send — the provider answering Left, or anything thrown — keeps the hold, so the user is invited the
    /// next time they are saved active, reactivated, or their organisation is reactivated. It never fails the
    /// change it belongs to.
    /// </remarks>
    public async Task SendHeldAsync(User user)
    {
        try
        {
            var result = await invitationEmailService.SendInvitationAsync(user.Email, user.Name, user.Role, CancellationToken.None);
            if (result.IsLeft)
            {
                result.IfLeft(e => logger.LogWarning(e, "Held invitation email failed for {Email}; the hold is kept so it is retried", user.Email));
                return;
            }

            user.ReleaseHeldInvitation(dateTimeProvider.GetUtcNow());
            await userRepository.UpdateAsync(user, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not send the held invitation for {UserId}; the hold is kept unless it was sent", user.Id.Value);
        }
    }

    /// <summary>
    /// Call once an organisation is reactivated and saved, with its active users. Sends each one's held invitation —
    /// a user reactivated, or added, while the organisation was still deactivated. Users still deactivated are not
    /// passed in, so they keep theirs until they are reactivated themselves.
    /// </summary>
    public async Task SendHeldOnOrganisationReactivationAsync(IEnumerable<User> activeUsers)
    {
        foreach (var user in activeUsers.Where(u => u.InvitationHeldAt is not null))
        {
            await SendHeldAsync(user);
        }
    }

    private static bool IsDeactivated(Organisation? organisation) =>
        organisation is not null && organisation.OnboardingStatus.ToString() == OnboardingStatuses.Suspended;

    private async Task<bool> IsInADeactivatedOrganisationAsync(User user, CancellationToken cancellationToken)
    {
        var organisationId = await organisationUserRepository.FindOrganisationIdForUser(user.Id, cancellationToken);
        if (organisationId.IsNone)
        {
            // A DHSC user belongs to no organisation, so only their own status decides whether they can sign in.
            return false;
        }

        var organisation = await organisationRepository.GetOrganisationById(
            organisationId.Match(id => id, () => OrganisationId.Empty()),
            cancellationToken);
        return organisation.Match(IsDeactivated, () => false);
    }
}